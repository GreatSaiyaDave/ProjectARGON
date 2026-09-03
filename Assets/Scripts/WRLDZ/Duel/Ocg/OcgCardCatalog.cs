using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using UnityEngine;

namespace WRLDZ.Duel.Ocg
{
    public enum OcgCardCatalogBackend
    {
        None = 0,
        Sqlite = 1,
        JsonFallback = 2
    }

    /// <summary>v11 OCG_CardData fields. setcodes is the uint16 sequence without terminator.</summary>
    public sealed class OcgCardData
    {
        public uint Code;
        public uint Alias;
        public ushort[] Setcodes;
        public uint Type;
        public uint Level;
        public uint Attribute;
        public ulong Race;
        public int Attack;
        public int Defense;
        public uint Lscale;
        public uint Rscale;
        public uint LinkMarker;

        public bool FieldEqual(OcgCardData other)
        {
            if (other == null) return false;
            if (Code != other.Code || Alias != other.Alias || Type != other.Type) return false;
            if (Level != other.Level || Attribute != other.Attribute || Race != other.Race) return false;
            if (Attack != other.Attack || Defense != other.Defense) return false;
            if (Lscale != other.Lscale || Rscale != other.Rscale || LinkMarker != other.LinkMarker) return false;
            var a = Setcodes ?? Array.Empty<ushort>();
            var b = other.Setcodes ?? Array.Empty<ushort>();
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>Managed reader for the official CDB. Native uses sqlite only; JSON is stub/old tests.</summary>
    public static class OcgCardCatalog
    {
        public const uint TypeLink = 0x4000000;

        public static OcgCardCatalogBackend LastBackend { get; private set; }

        static bool _sqliteTried;
        static bool _sqliteOk;
        static Dictionary<uint, OcgCardData> _json;

        public static bool SqliteReady
        {
            get
            {
                EnsureSqliteInit();
                return _sqliteOk;
            }
        }

        public static bool TryGet(uint code, out OcgCardData data)
        {
            data = null;
            if (TrySqlite(code, out data))
            {
                LastBackend = OcgCardCatalogBackend.Sqlite;
                return data != null && data.Code != 0;
            }
            EnsureJson();
            LastBackend = OcgCardCatalogBackend.JsonFallback;
            if (_json != null && _json.TryGetValue(code, out data))
                return true;
            Debug.LogWarning("[WRLDZ OCG] missing card data for code " + code);
            data = new OcgCardData { Code = 0, Setcodes = Array.Empty<ushort>() };
            return false;
        }

        public static bool TryGetFromSqlite(uint code, out OcgCardData data)
        {
            data = null;
            if (!TrySqlite(code, out data))
                return false;
            LastBackend = OcgCardCatalogBackend.Sqlite;
            return data != null && data.Code != 0;
        }

        public static bool TryGetFromJson(uint code, out OcgCardData data)
        {
            EnsureJson();
            if (_json != null && _json.TryGetValue(code, out data))
                return true;
            data = null;
            return false;
        }

        public static IEnumerable<uint> JsonCodes()
        {
            EnsureJson();
            return _json != null ? _json.Keys : Array.Empty<uint>();
        }

        static bool TrySqlite(uint code, out OcgCardData data)
        {
            data = null;
            EnsureSqliteInit();
            if (!_sqliteOk) return false;
            try
            {
                using (var conn = new SqliteConnection("Data Source=" + CdbPath()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT id, alias, setcode, type, atk, def, level, race, attribute FROM datas WHERE id = $id";
                        cmd.Parameters.AddWithValue("$id", (long)code);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read())
                                return true; // sqlite path exercised, missing row
                            data = FromDatasRow(
                                (uint)r.GetInt64(0),
                                (uint)r.GetInt64(1),
                                unchecked((ulong)r.GetInt64(2)),
                                (uint)r.GetInt64(3),
                                r.GetInt32(4),
                                r.GetInt32(5),
                                (uint)r.GetInt64(6),
                                (ulong)r.GetInt64(7),
                                (uint)r.GetInt64(8));
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ OCG] sqlite read failed: " + ex.Message);
                _sqliteOk = false;
                return false;
            }
        }

        static void EnsureSqliteInit()
        {
            if (_sqliteTried) return;
            _sqliteTried = true;
            try
            {
                SQLitePCL.Batteries_V2.Init();
                _sqliteOk = File.Exists(CdbPath());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ OCG] sqlite init failed: " + ex.Message);
                _sqliteOk = false;
            }
        }

        static string CdbPath() =>
            Path.Combine(Application.streamingAssetsPath, "OcgCore", "cards.cdb");

        internal static OcgCardData FromDatasRow(uint id, uint alias, ulong setcode, uint type,
            int atk, int def, uint levelPacked, ulong race, uint attribute)
        {
            var data = new OcgCardData
            {
                Code = id,
                Alias = alias,
                Setcodes = UnpackSetcodes(setcode),
                Type = type,
                Level = levelPacked & 0xff,
                Attribute = attribute,
                Race = race,
                Attack = atk,
                Defense = def,
                Lscale = (levelPacked >> 24) & 0xff,
                Rscale = (levelPacked >> 16) & 0xff,
                LinkMarker = 0
            };
            if ((data.Type & TypeLink) != 0)
            {
                data.LinkMarker = (uint)data.Defense;
                data.Defense = 0;
            }
            return data;
        }

        internal static ushort[] UnpackSetcodes(ulong packed)
        {
            var list = new List<ushort>(4);
            var v = packed;
            while (v != 0)
            {
                var sc = (ushort)(v & 0xffff);
                if (sc != 0) list.Add(sc);
                v >>= 16;
            }
            return list.ToArray();
        }

        static void EnsureJson()
        {
            if (_json != null) return;
            _json = new Dictionary<uint, OcgCardData>();
            var path = Path.Combine(Application.streamingAssetsPath, "OcgCore", "cards_datas.json");
            if (!File.Exists(path)) return;
            var file = JsonUtility.FromJson<JsonFile>(File.ReadAllText(path));
            if (file?.cards == null) return;
            foreach (var row in file.cards)
            {
                var data = new OcgCardData
                {
                    Code = (uint)row.code,
                    Alias = (uint)row.alias,
                    Setcodes = ToUshorts(row.setcodes),
                    Type = (uint)row.type,
                    Level = (uint)row.level,
                    Attribute = (uint)row.attribute,
                    Race = (ulong)row.race,
                    Attack = row.attack,
                    Defense = row.defense,
                    Lscale = (uint)row.lscale,
                    Rscale = (uint)row.rscale,
                    LinkMarker = (uint)row.link_marker
                };
                _json[data.Code] = data;
            }
        }

        static ushort[] ToUshorts(int[] src)
        {
            if (src == null || src.Length == 0) return Array.Empty<ushort>();
            var a = new ushort[src.Length];
            for (var i = 0; i < src.Length; i++) a[i] = (ushort)src[i];
            return a;
        }

        [Serializable]
        class JsonFile
        {
            public JsonCard[] cards;
        }

        [Serializable]
        class JsonCard
        {
            public int code;
            public int alias;
            public int[] setcodes;
            public int type;
            public int level;
            public int attribute;
            public long race;
            public int attack;
            public int defense;
            public int lscale;
            public int rscale;
            public int link_marker;
        }
    }
}
