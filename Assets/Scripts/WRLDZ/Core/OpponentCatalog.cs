using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Duel;
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
        /// <summary>Group key: the duelist this deck belongs to (several decks per duelist).</summary>
        public string character;
        /// <summary>The deck's own name within its duelist's list ("Grandpa's Deck", "The Forbidden One").</summary>
        public string deckName;
        /// <summary>A tag team's duelists ("Para", "Dox"); empty = one duelist.</summary>
        public string[] members;
        /// <summary>0 = the format default (8000).</summary>
        public int startingLp;
        /// <summary>The name on the seat (a street duelist); empty = <see cref="name"/>.</summary>
        public string displayName;
        /// <summary>AI difficulty tier for this opponent (Easy / Normal / Hard / Expert).</summary>
        public AiDifficulty difficulty = AiDifficulty.Normal;
    }

    public sealed class OpponentGroup
    {
        public string character;
        public string name;
        public string[] members;
        public readonly List<OpponentDef> decks = new();
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
            if (!StreamingFiles.TryReadAllText(RelativePath, out var json))
            {
                Debug.LogWarning("[WRLDZ Opponents] Missing " + StreamingFiles.FullPathForLog(RelativePath));
                _cached = new OpponentRosterFile { opponents = Array.Empty<OpponentDef>() };
                return _cached;
            }

            try
            {
                _cached = JsonUtility.FromJson<OpponentRosterFile>(json)
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

        /// <summary>The roster grouped by duelist, in file order (the Vs AI list).</summary>
        public static List<OpponentGroup> Groups()
        {
            var groups = new List<OpponentGroup>();
            foreach (var o in All())
            {
                var key = !string.IsNullOrEmpty(o.character) ? o.character : o.id;
                var g = groups.Find(x => x.character == key);
                if (g == null)
                {
                    g = new OpponentGroup { character = key, name = o.name, members = o.members };
                    groups.Add(g);
                }
                if (g.members == null || g.members.Length == 0) g.members = o.members;
                g.decks.Add(o);
            }
            return groups;
        }

        /// <summary>A street duelist from <see cref="Data.NpcDuelistRoster"/> as a pickable opponent.</summary>
        public static OpponentDef ForStreet(Data.NpcDuelistRoster.NpcDuelist npc)
        {
            var lp = npc.Band == StreetLpBand.Street8000 ? MapZoneCatalog.Street8000Lp : MapZoneCatalog.Street4000Lp;
            return new OpponentDef
            {
                id = "street:" + npc.Name, name = npc.Name, displayName = npc.Name, character = "street:" + npc.Name,
                deckName = DeckTitle(npc.DeckFor(npc.Band)), file = npc.DeckFor(npc.Band), erazBandId = ErazFormat.Original,
                notes = npc.ArchetypeLabel + " · " + lp + " LP", startingLp = lp,
            };
        }

        static string DeckTitle(string file)
        {
            var n = (file ?? "").Replace("wander_", "").Replace("4000_", "").Replace("8000_", "").Replace("play_", "").Replace(".json", "").Replace("_", " ");
            return n.Length > 0 ? char.ToUpperInvariant(n[0]) + n.Substring(1) : n;
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
            return StreamingFiles.Exists("Decks/" + fileName);
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
            c.FormatTitle = opp != null ? "vs " + opp.name + (string.IsNullOrEmpty(opp.deckName) ? "" : " · " + opp.deckName) : "Player vs AI";
            if (opp != null)
            {
                // A tag team keeps the group name on the seat (Paradox Brothers). displayName is the
                // brother shown on that deck's row (Para or Dox); the bodies still come from members.
                c.OpponentDisplayName = opp.members != null && opp.members.Length > 1
                    ? opp.name
                    : (!string.IsNullOrEmpty(opp.displayName) ? opp.displayName : opp.name);
                c.OpponentMembers = opp.members != null && opp.members.Length > 1 ? string.Join("|", opp.members) : "";
                if (opp.startingLp >= 1000) c.StartingLp = opp.startingLp;
            }
            c.EntrySource = labTest ? AppSession.SceneBoot : AppSession.SceneMainMenu;
            c.Difficulty = opp != null
                ? OpponentDifficultyPrefs.Get(opp)
                : AiDifficulty.Normal;
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
