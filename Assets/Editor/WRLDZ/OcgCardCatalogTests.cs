using System;
using System.IO;
using UnityEngine;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    public static class OcgCardCatalogTests
    {
        const int DarkHole = 53129443;
        const int Reaper = 85684223;
        const int Polymerization = 24094653;
        const int Stardust = 44508094;
        const int Utopia = 84013237;
        const int DecodeTalker = 1861629;
        const int BattleOxOfficial = 5053192;

        public static void SqlitePathExercised()
        {
            if (!OcgCardCatalog.SqliteReady)
                throw new Exception("sqlite/CDB not ready");
            if (!OcgCardCatalog.TryGetFromSqlite(DarkHole, out var data) || data == null || data.Code != DarkHole)
                throw new Exception("sqlite TryGet Dark Hole failed");
            if (OcgCardCatalog.LastBackend != OcgCardCatalogBackend.Sqlite)
                throw new Exception("LastBackend is " + OcgCardCatalog.LastBackend + ", expected Sqlite");
            if (data.Type != 2)
                throw new Exception("Dark Hole type " + data.Type);
        }

        public static void FullOfficialSamples()
        {
            MustSqlite(Reaper, "Reaper on the Nightmare");
            MustSqlite(Polymerization, "Polymerization");
            MustSqlite(Stardust, "Stardust Dragon");
            MustSqlite(Utopia, "Number 39: Utopia");
            MustSqlite(DecodeTalker, "Decode Talker");
            MustSqlite(BattleOxOfficial, "Battle Ox 5053192");
        }

        public static void ExtraIndexJoinedToCdb()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "OcgCore", "extra_index.csv");
            if (!File.Exists(path))
                throw new Exception("missing extra_index.csv");
            var lines = File.ReadAllLines(path);
            if (lines.Length - 1 < 2000)
                throw new Exception("extra_index rows " + (lines.Length - 1) + " < 2000");
            var sawReaper = false;
            var sample = 0;
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                var comma = line.IndexOf(',');
                if (comma <= 0) throw new Exception("bad extra_index line " + i);
                if (!uint.TryParse(line.Substring(0, comma), out var id))
                    throw new Exception("bad extra_index id line " + i);
                if (id == Reaper) sawReaper = true;
                if (sample == 0) sample = (int)id;
            }
            if (!sawReaper)
                throw new Exception("extra_index missing Reaper 85684223");
            if (!OcgCardCatalog.TryGetFromSqlite((uint)sample, out var row) || row == null)
                throw new Exception("random extra_index id " + sample + " has no datas row");
        }

        public static void CdbJsonParity()
        {
            var n = 0;
            foreach (var code in OcgCardCatalog.JsonCodes())
            {
                if (!OcgCardCatalog.TryGetFromSqlite(code, out var fromCdb) || fromCdb == null)
                    throw new Exception("cdb missing " + code);
                if (!OcgCardCatalog.TryGetFromJson(code, out var fromJson) || fromJson == null)
                    throw new Exception("json missing " + code);
                if (!fromCdb.FieldEqual(fromJson) && code != BattleOxOfficial && code != 5053103)
                    throw new Exception("parity mismatch " + code);
                n++;
            }
            if (n < 19)
                throw new Exception("expected 19 lab cards in json stub, got " + n);
        }

        static void MustSqlite(int id, string label)
        {
            if (!OcgCardCatalog.TryGetFromSqlite((uint)id, out var data) || data == null || data.Code != (uint)id)
                throw new Exception("sqlite TryGet " + label + " (" + id + ") failed");
        }
    }
}
