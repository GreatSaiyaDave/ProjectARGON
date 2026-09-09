using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    [Serializable]
    public class OpponentDef
    {
        public string id;
        public string name;
        public string file;
        public string erazBandId;
        public string notes;
    }

    [Serializable]
    public class OpponentRosterFile
    {
        public int version;
        public string notes;
        public OpponentDef[] opponents;
    }

    /// <summary>
    /// Selectable PvAI / lab opponents. Standard TCG. Never includes Lab Kaiba.
    /// </summary>
    public static class OpponentCatalog
    {
        public const string RelativePath = "Decks/opponent_roster.json";
        public const string BannedLabKaibaFile = "ai_kaiba.json";
        public const string DefaultFile = "character_dk_kaiba.json";

        /// <summary>Hub VS AI pick, consumed by the surface-scan create sheet.</summary>
        public static OpponentDef Pending;

        static OpponentRosterFile _cached;

        public static void Invalidate() => _cached = null;

        public static OpponentRosterFile Load()
        {
            if (_cached != null) return _cached;
            var path = Path.Combine(Application.streamingAssetsPath, RelativePath);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ Opponents] Missing " + path);
                _cached = new OpponentRosterFile { opponents = Array.Empty<OpponentDef>() };
                return _cached;
            }

            try
            {
                _cached = JsonUtility.FromJson<OpponentRosterFile>(File.ReadAllText(path))
                          ?? new OpponentRosterFile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Opponents] Parse failed: " + ex.Message);
                _cached = new OpponentRosterFile();
            }

            _cached.opponents ??= Array.Empty<OpponentDef>();
            return _cached;
        }

        public static IReadOnlyList<OpponentDef> All()
        {
            var file = Load();
            var list = new List<OpponentDef>();
            if (file.opponents == null) return list;
            for (var i = 0; i < file.opponents.Length; i++)
            {
                var o = file.opponents[i];
                if (o == null || string.IsNullOrEmpty(o.file)) continue;
                if (IsBanned(o.file)) continue;
                list.Add(o);
            }

            return list;
        }

        public static OpponentDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All();
            for (var i = 0; i < all.Count; i++)
                if (string.Equals(all[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        public static bool DeckFileExists(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var path = Path.Combine(Application.streamingAssetsPath, "Decks", fileName);
            return File.Exists(path);
        }

        public static bool IsBanned(string fileName) =>
            string.Equals(fileName, BannedLabKaibaFile, StringComparison.OrdinalIgnoreCase);

        public static ArDuelMatchConfig MakeMatch(OpponentDef opp, bool labTest, bool digital)
        {
            var c = labTest ? ArDuelMatchConfig.Practice() : ArDuelMatchConfig.DefaultQuick();
            c.Launch = labTest ? ArDuelLaunchKind.LabTest : ArDuelLaunchKind.Hub;
            c.Opponent = ArDuelOpponentKind.AiLocal;
            c.FormatId = "pvai";
            c.PreferDigital = digital;
            c.DkOverlay = false;
            c.StartingLp = 8000;
            c.AiDeckFile = opp != null && !IsBanned(opp.file) ? opp.file : DefaultFile;
            c.ErazBandId = !string.IsNullOrEmpty(opp?.erazBandId) ? opp.erazBandId : ErazFormat.Original;
            c.FormatTitle = opp != null ? "vs " + opp.name : "Player vs AI";
            c.EntrySource = labTest ? AppSession.SceneBoot : AppSession.SceneMainMenu;
            c.SkipPreDuelCinematic = labTest;
            c.ClampSeparation();
            return c;
        }

        public static void ApplyPending(ArDuelMatchConfig cfg)
        {
            if (cfg == null) return;
            var opp = Pending;
            if (opp == null || string.IsNullOrEmpty(opp.file) || IsBanned(opp.file))
            {
                if (string.IsNullOrEmpty(cfg.AiDeckFile) || IsBanned(cfg.AiDeckFile))
                    cfg.AiDeckFile = DefaultFile;
                return;
            }

            cfg.AiDeckFile = opp.file;
            if (!string.IsNullOrEmpty(opp.erazBandId))
                cfg.ErazBandId = opp.erazBandId;
            if (string.IsNullOrEmpty(cfg.FormatTitle) ||
                cfg.FormatTitle.StartsWith("Player vs AI", StringComparison.OrdinalIgnoreCase))
                cfg.FormatTitle = "vs " + opp.name;
        }

        public static void ClearPending() => Pending = null;
    }
}
