using System;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// One duel format as the pre-duel setup offers it. This is a VIEW over the rules the project already defines —
    /// it names which existing pieces apply (starting LP, <see cref="DuelRulesOverlay"/>, which Forbidden &amp;
    /// Limited list the deck check uses) and never carries rules of its own.
    /// </summary>
    public sealed class DuelFormatDef
    {
        public string Id;
        public string Title;
        /// <summary>Two-line rules summary on the card (UI_SPEC §3.5: title, rules, LP, LOCKED or PLAY).</summary>
        public string Rules;
        public int StartingLp;
        /// <summary>Duelist Kingdom table laws (<see cref="DuelRulesOverlay.DuelistKingdomTable"/>).</summary>
        public bool DkOverlay;
        /// <summary>Deck check against the current Advanced Format list (<see cref="OfficialDataSources"/>);
        /// otherwise the chosen ERAZ band's end-of-era list.</summary>
        public bool AdvancedBanlist;
        /// <summary>Playable today. Everything else is listed but LOCKED (the project's locked-option convention).</summary>
        public bool Open;
        /// <summary>Why a closed format is locked, shown on its card.</summary>
        public string LockReason;
        /// <summary><see cref="FormatProgress"/> badge id when the format is a badge format, else null.</summary>
        public string BadgeId;

        public string LpLine => StartingLp + " LP";
    }

    /// <summary>
    /// The duel formats the pre-duel setup lists. Only Standard TCG and Advanced TCG are open for now; the rest stay
    /// visible and locked (GDD §Formats: Standard TCG is the default; Duelist Kingdom and the badge formats are
    /// not live). Ids are stable strings on <see cref="ArDuelMatchConfig.RulesFormatId"/>.
    /// </summary>
    public static class DuelFormatCatalog
    {
        public const string StandardTcg = "standard";
        public const string AdvancedTcg = "advanced";
        public const string QuickDuel = "quick";
        public const string DuelistKingdom = "dk";

        static DuelFormatDef[] _all;

        public static DuelFormatDef[] All
        {
            get
            {
                if (_all != null) return _all;
                var list = new System.Collections.Generic.List<DuelFormatDef>
                {
                    new DuelFormatDef
                    {
                        Id = StandardTcg, Title = "Standard TCG",
                        Rules = "Official TCG rules · the era's end-of-format Forbidden & Limited list",
                        StartingLp = TcgRules.StartingLifePoints, Open = true
                    },
                    new DuelFormatDef
                    {
                        Id = AdvancedTcg, Title = "Advanced TCG",
                        Rules = "Official TCG rules · the current Advanced Format Forbidden & Limited list",
                        StartingLp = TcgRules.StartingLifePoints, AdvancedBanlist = true, Open = true
                    },
                    new DuelFormatDef
                    {
                        Id = QuickDuel, Title = "Quick Duel",
                        Rules = "Street format · short duels",
                        StartingLp = 4000, Open = false, LockReason = "Not live yet"
                    },
                    new DuelFormatDef
                    {
                        Id = DuelistKingdom, Title = FormatProgress.Title("dk"),
                        Rules = "Season 1 table laws · no direct attacks · tribute-free summons",
                        StartingLp = DuelRulesOverlay.DuelistKingdomTable().OverrideStartingLp,
                        DkOverlay = true, Open = false, LockReason = "Format badge · story progress", BadgeId = "dk"
                    }
                };
                for (var i = 0; i < FormatProgress.Ids.Length; i++)
                {
                    var id = FormatProgress.Ids[i];
                    if (string.Equals(id, DuelistKingdom, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new DuelFormatDef
                    {
                        Id = id, Title = FormatProgress.Titles[i],
                        Rules = "Format badge · table laws are not live yet",
                        StartingLp = TcgRules.StartingLifePoints, Open = false,
                        LockReason = "Badge · rules not final", BadgeId = id
                    });
                }

                _all = list.ToArray();
                return _all;
            }
        }

        /// <summary>The format for an id; Standard TCG for an empty or unknown id (what every duel played before).</summary>
        public static DuelFormatDef Get(string id)
        {
            var all = All;
            if (!string.IsNullOrEmpty(id))
                for (var i = 0; i < all.Length; i++)
                    if (string.Equals(all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                        return all[i];
            return all[0];
        }

        /// <summary>Write the format's existing rule inputs onto the match (LP, DK overlay); nothing else changes.</summary>
        public static void Apply(ArDuelMatchConfig cfg, DuelFormatDef f)
        {
            if (cfg == null || f == null) return;
            cfg.RulesFormatId = f.Id;
            cfg.StartingLp = f.StartingLp;
            cfg.DkOverlay = f.DkOverlay;
        }

        /// <summary>
        /// The setup sheet's confirmed choice. When the launch dictates the format (a street 4000 LP duel, a Tear or
        /// Raid boss's LP, a story stage, the DK overlay) the encounter keeps its own starting LP and overlay and only
        /// an empty rules id is filled in; applying the catalog's def there used to overwrite a 4000 LP street duel
        /// with 8000. Otherwise the chosen def is applied in full.
        /// </summary>
        public static void ApplyChoice(ArDuelMatchConfig cfg, string chosenFormatId)
        {
            if (cfg == null) return;
            var dictated = DictatedBy(cfg);
            if (dictated == null)
            {
                Apply(cfg, Get(chosenFormatId));
                return;
            }

            if (string.IsNullOrEmpty(cfg.RulesFormatId)) cfg.RulesFormatId = dictated.Id;
        }

        /// <summary>
        /// The match's format when a launch dictates one (a boss LP, the DK overlay, a story stage): the setup shows it
        /// fixed. Null when the player may choose.
        /// </summary>
        public static DuelFormatDef DictatedBy(ArDuelMatchConfig cfg)
        {
            if (cfg == null) return null;
            if (cfg.DkOverlay) return Get(DuelistKingdom);
            switch (cfg.Launch)
            {
                case ArDuelLaunchKind.TearBoss:
                case ArDuelLaunchKind.RaidBoss:
                case ArDuelLaunchKind.StoryEra:
                case ArDuelLaunchKind.PvpZone:
                case ArDuelLaunchKind.Tournament:
                case ArDuelLaunchKind.NpcStreet:
                    return Get(string.IsNullOrEmpty(cfg.RulesFormatId) ? StandardTcg : cfg.RulesFormatId);
                default:
                    return null;
            }
        }

        /// <summary>Deck check list for this match: the Advanced list for Advanced TCG (and the modern band), else the era's.</summary>
        public static bool UsesAdvancedBanlist(ArDuelMatchConfig cfg)
        {
            if (cfg == null) return false;
            return Get(cfg.RulesFormatId).AdvancedBanlist || OfficialDataSources.UsesAdvancedBanlist(cfg.ErazBandId);
        }
    }
}
