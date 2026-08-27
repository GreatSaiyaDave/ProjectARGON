using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Official data acquisition paths. Card text lives in StreamingAssets (Yugipedia/Konami-aligned).
    /// Banlist / future OCR feed attach here without changing engine core.
    ///
    /// Sources (human authority):
    /// · https://yugipedia.com/ — card pages, rulings, glossary
    /// · https://www.yugioh-card.com/en/limited/ — Advanced Format banlist
    /// · https://www.db.yugioh-card.com/yugiohdb/ — Konami card DB
    /// · Local: StreamingAssets/Cards/cards_db.json
    /// </summary>
    public static class OfficialDataSources
    {
        public const string YugipediaBase = "https://yugipedia.com/wiki/";
        public const string KonamiBanlist = "https://www.yugioh-card.com/en/limited/";
        public const string KonamiDb = "https://www.db.yugioh-card.com/yugiohdb/";

        public static string LocalCardDbPath =>
            Path.Combine(Application.streamingAssetsPath, "Cards", "cards_db.json");

        public static string LocalBanlistPath =>
            Path.Combine(Application.streamingAssetsPath, "Cards", "banlist_advanced.json");

        public static string LocalRulingsNotesPath =>
            Path.Combine(Application.streamingAssetsPath, "Cards", "RULINGS_NOTES.md");

        /// <summary>Placeholder card record ready for Wiki/Konami population or OCR merge.</summary>
        [Serializable]
        public class OfficialCardRecord
        {
            public int passcode;
            public string name;
            public string text;
            public string cardType;
            public string attribute;
            public string race;
            public int levelOrRankOrLink;
            public int atk;
            public int def;
            public string frameType;
            public string banlistStatus; // Unlimited / Semi-Limited / Limited / Forbidden
            public string yugipediaTitle;
            public string textHash;
            /// <summary>Future: OCR crop id / physical scan key.</summary>
            public string physicalScanKey;
        }

        [Serializable]
        public class BanlistFile
        {
            public string format = "Advanced";
            public string effectiveDate = "";
            public string sourceUrl = KonamiBanlist;
            public int[] forbidden = Array.Empty<int>();
            public int[] limited = Array.Empty<int>();
            public int[] semiLimited = Array.Empty<int>();
        }

        static BanlistFile _banlist;

        public static BanlistFile LoadBanlist()
        {
            if (_banlist != null) return _banlist;
            var path = LocalBanlistPath;
            if (File.Exists(path))
            {
                try
                {
                    _banlist = JsonUtility.FromJson<BanlistFile>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ] Banlist parse failed: " + ex.Message);
                }
            }

            _banlist ??= new BanlistFile
            {
                format = "Advanced",
                effectiveDate = "local-stub",
                sourceUrl = KonamiBanlist,
                // Empty until populated from official list — engine treats missing as Unlimited
                forbidden = Array.Empty<int>(),
                limited = Array.Empty<int>(),
                semiLimited = Array.Empty<int>()
            };
            return _banlist;
        }

        public static BanlistStatus StatusOf(int passcode)
        {
            var b = LoadBanlist();
            if (b.forbidden != null && Array.IndexOf(b.forbidden, passcode) >= 0)
                return BanlistStatus.Forbidden;
            if (b.limited != null && Array.IndexOf(b.limited, passcode) >= 0)
                return BanlistStatus.Limited;
            if (b.semiLimited != null && Array.IndexOf(b.semiLimited, passcode) >= 0)
                return BanlistStatus.SemiLimited;
            return BanlistStatus.Unlimited;
        }

        public static int MaxCopies(int passcode) => StatusOf(passcode) switch
        {
            BanlistStatus.Forbidden => 0,
            BanlistStatus.Limited => 1,
            BanlistStatus.SemiLimited => 2,
            _ => TcgRules.MaxCopiesPerCard
        };

        /// <summary>Era-aware copy cap; currently delegates to global Advanced stub.</summary>
        public static int MaxCopies(int passcode, string eraId) => MaxCopies(passcode);

        public static OfficialCardRecord FromCardDef(CardDef def)
        {
            if (def == null) return null;
            return new OfficialCardRecord
            {
                passcode = def.id,
                name = def.name,
                text = OfficialCardAuthority.OfficialText(def),
                cardType = def.type,
                attribute = def.attribute,
                race = def.race,
                levelOrRankOrLink = def.level,
                atk = def.atk,
                def = def.def,
                frameType = def.frameType,
                banlistStatus = StatusOf(def.id).ToString(),
                textHash = OfficialCardAuthority.TextHash(def),
                yugipediaTitle = def.name?.Replace(' ', '_')
            };
        }
    }

    public enum BanlistStatus
    {
        Unlimited = 0,
        SemiLimited = 1,
        Limited = 2,
        Forbidden = 3
    }
}
