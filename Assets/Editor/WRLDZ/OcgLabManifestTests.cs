using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    /// <summary>Open-pool fixture: CDB membership, deck sizes, Battle Ox 5053192.</summary>
    public static class OcgLabManifestTests
    {
        const int OfficialBattleOx = 5053192;
        const int WrongBattleOx = 5053103;
        const int FakeId = 1;
        const int StardustDragon = 44508094;

        [Serializable]
        class ManifestFile
        {
            public string mode;
            public string playPool;
            public string tcgPool;
            public string scriptsCommit;
            public string cdbKind;
            public string extraIndex;
            public string[] sourceDecks;
            public ManifestRules rules;
            public string[] notes;
        }

        [Serializable]
        class ManifestRules
        {
            public int extraMax;
            public int mainMin;
        }

        public static void AssertValid()
        {
            var manifestPath = Path.Combine(Application.streamingAssetsPath, "OcgCore/lab_card_manifest.json");
            if (!File.Exists(manifestPath))
                throw new Exception("missing lab_card_manifest.json");
            var man = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(manifestPath));
            if (man == null)
                throw new Exception("manifest parse failed");
            if (man.mode != "full_official_pool")
                throw new Exception("mode must be full_official_pool, got " + man.mode);
            if (man.playPool != "tcg_only")
                throw new Exception("playPool must be tcg_only, got " + man.playPool);
            if (string.IsNullOrEmpty(man.tcgPool) || man.tcgPool.IndexOf("tcg_pool", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("tcgPool must point at tcg_pool.json");
            var tcgPath = Path.Combine(Application.streamingAssetsPath, "OcgCore/tcg_pool.json");
            if (!File.Exists(tcgPath))
                throw new Exception("missing tcg_pool.json");
            if (string.IsNullOrEmpty(man.scriptsCommit))
                throw new Exception("scriptsCommit required");
            if (string.IsNullOrEmpty(man.cdbKind) || man.cdbKind.IndexOf("full", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("cdbKind must describe full datas+texts");
            if (man.sourceDecks == null || man.sourceDecks.Length != 2)
                throw new Exception("sourceDecks must list both lab decks");
            if (man.rules == null)
                throw new Exception("missing rules");
            if (man.rules.extraMax != 15)
                throw new Exception("extraMax must be 15");
            if (man.rules.mainMin != 40)
                throw new Exception("mainMin must be 40");

            if (OcgCardCatalog.TryGetFromSqlite(FakeId, out var fake) && fake != null && fake.Code != 0)
                throw new Exception("fake id 1 must not have a datas row");
            if (!OcgCardCatalog.TryGetFromSqlite(StardustDragon, out var stardust) || stardust == null)
                throw new Exception("Stardust 44508094 must exist in CDB without a manifest card row");

            var deckFiles = new HashSet<string>(man.sourceDecks);
            var sawOx = false;
            foreach (var file in man.sourceDecks)
            {
                var path = Path.Combine(Application.streamingAssetsPath, "Decks", file);
                if (!File.Exists(path))
                    throw new Exception("missing deck " + file);
                var deck = JsonUtility.FromJson<DeckFile>(File.ReadAllText(path));
                if (deck == null)
                    throw new Exception("deck parse failed " + file);
                var parsed = new Dictionary<int, int>();
                var mainTotal = SumEntries(deck.main, parsed);
                var extraTotal = SumEntries(deck.extra, parsed);
                if (parsed.ContainsKey(WrongBattleOx))
                    throw new Exception(file + " has wrong Battle Ox id 5053103");
                if (parsed.ContainsKey(OfficialBattleOx))
                    sawOx = true;
                if (mainTotal < man.rules.mainMin)
                    throw new Exception(file + " main " + mainTotal + " < " + man.rules.mainMin);
                if (extraTotal > man.rules.extraMax)
                    throw new Exception(file + " extra " + extraTotal + " > " + man.rules.extraMax);
                foreach (var id in parsed.Keys)
                {
                    if (!OcgCardCatalog.TryGetFromSqlite((uint)id, out var data) || data == null || data.Code == 0)
                        throw new Exception(file + " id " + id + " missing from cards.cdb");
                }
                if (!deckFiles.Contains(file))
                    throw new Exception("manifest deck not in sourceDecks: " + file);
            }
            if (!sawOx)
                throw new Exception("lab decks must include Battle Ox 5053192");
        }

        static int SumEntries(DeckCardEntry[] entries, Dictionary<int, int> into)
        {
            if (entries == null) return 0;
            var n = 0;
            foreach (var e in entries)
            {
                n += e.qty;
                if (into == null) continue;
                if (into.ContainsKey(e.id))
                    into[e.id] += e.qty;
                else
                    into[e.id] = e.qty;
            }
            return n;
        }
    }
}
