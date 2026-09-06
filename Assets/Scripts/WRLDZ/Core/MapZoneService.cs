using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Lightweight zone interaction outcomes (loot / SE / training launch).
    /// Full spatial Bazaar & raid multiplayer remain stubs → menu shells.
    /// </summary>
    public static class MapZoneService
    {
        const string PrefLootPrefix = "wrldz.zoneLoot.";

        /// <summary>Grant small walk-adjacent SE from Anchor / Treasure (daily soft lock per pin).</summary>
        public static bool TryLootGrant(string zoneId, MapZoneKind kind, out int seGained, out string toast)
        {
            seGained = 0;
            toast = "";
            var acc = AppSession.Ensure()?.Account;
            if (acc == null)
            {
                toast = "No session";
                return false;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            var key = PrefLootPrefix + (zoneId ?? kind.ToString()) + "." +
                      System.DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (PlayerPrefs.GetInt(key, 0) > 0)
            {
                toast = MapZoneCatalog.Label(kind);
                return false;
            }

            seGained = kind switch
            {
                MapZoneKind.Treasure => 2,
                MapZoneKind.Anchor => 1,
                MapZoneKind.Event => 1,
                _ => 1
            };

            ArtifactService.GrantUntaggedSetEnergy(acc.progress, acc.inventory, seGained);
            ProgressionService.Persist(acc);
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
            toast = $"+{seGained} SE";
            return true;
        }

        public static ArDuelMatchConfig MakeTrainingConfig(string zoneTitle)
        {
            var c = ArDuelMatchConfig.Practice();
            c.Opponent = ArDuelOpponentKind.AiLocal;
            c.EntrySource = AppSession.SceneOverworld;
            c.FormatTitle = string.IsNullOrEmpty(zoneTitle) ? "Training Zone · 0 XP" : "Training · " + zoneTitle;
            c.StartingLp = 8000;
            ApplyLastSurfaceScanIfAny(c);
            return c;
        }

        public static ArDuelMatchConfig MakeTearConfig(string zoneId, string zoneTitle, bool digital)
        {
            var cfg = ArDuelMatchConfig.FromTear(zoneId, zoneTitle, ArDuelMatchConfig.DefaultStandM);
            cfg.EntrySource = AppSession.SceneOverworld;
            cfg.PreferDigital = digital;
            cfg.FormatTitle = digital
                ? "Tear Zone · Digital · " + (zoneTitle ?? "Tear")
                : "Tear Zone · AR · " + (zoneTitle ?? "Tear");
            cfg.StartingLp = 8000;
            ApplyLastSurfaceScanIfAny(cfg);
            return cfg;
        }

        static readonly string[] Street4000Decks =
        {
            "wander_4000_beatdown.json",
            "wander_4000_insect.json",
            "wander_4000_water.json",
            "wander_4000_dino.json",
            "wander_4000_zombie.json",
            "wander_4000_machine.json",
            "wander_4000_stonewall.json",
            "wander_4000_jam.json",
            "wander_4000_play_harpie.json",
            "wander_4000_play_redeyes.json",
            "wander_4000_play_gemini.json",
            "wander_4000_play_scorpion.json",
            "wander_4000_play_union.json",
            "wander_4000_play_gaia.json",
            "wander_4000_play_ojama.json",
            "wander_4000_play_thunder.json",
            "wander_4000_play_searchers.json",
            "wander_4000_play_burn.json"
        };

        static readonly string[] Street8000Decks =
        {
            "wander_8000_harpie.json",
            "wander_8000_gravekeeper.json",
            "wander_8000_amazoness.json",
            "wander_8000_toon.json",
            "wander_8000_red_eyes.json",
            "wander_8000_monarch.json",
            "wander_8000_gravity.json",
            "wander_8000_labyrinth.json",
            "wander_8000_play_ninja.json",
            "wander_8000_play_horus.json",
            "wander_8000_play_armed.json",
            "wander_8000_play_mysticlv.json",
            "wander_8000_play_archfiend.json",
            "wander_8000_play_darkblade.json",
            "wander_8000_play_buster.json",
            "wander_8000_play_chaoslite.json",
            "wander_8000_play_gkbudget.json",
            "wander_8000_play_ultinsect.json"
        };

        /// <summary>Pick a themed street deck from StreamingAssets/Decks for this wanderer.</summary>
        public static string PickStreetDeck(string npcName, string tearId, StreetLpBand band)
        {
            var n = (npcName ?? "").ToLowerInvariant();
            var playground = n.Contains("schoolyard") || n.Contains("weekend")
                             || n.Contains("cafeteria") || n.Contains("library");
            if (band == StreetLpBand.Street8000)
            {
                if (playground) return Street8000Decks[StableIndex(n + "|" + tearId + "|play", Street8000Decks.Length)];
                if (n.Contains("plaza") || n.Contains("sentry")) return "wander_8000_gravity.json";
                if (n.Contains("gate") || n.Contains("guard")) return "wander_8000_labyrinth.json";
                if (n.Contains("rooftop")) return "wander_8000_harpie.json";
                if (n.Contains("rift") || n.Contains("magician")) return "wander_8000_gravekeeper.json";
                if (n.Contains("park") || n.Contains("corner")) return "wander_8000_amazoness.json";
                if (n.Contains("bus") || n.Contains("quiet")) return "wander_8000_toon.json";
                if (n.Contains("alley") || n.Contains("late")) return "wander_8000_red_eyes.json";
                if (n.Contains("night") || n.Contains("clerk")) return "wander_8000_monarch.json";
                return Street8000Decks[StableIndex(n + "|" + tearId, Street8000Decks.Length)];
            }

            if (playground) return Street4000Decks[StableIndex(n + "|" + tearId + "|play", Street4000Decks.Length)];
            if (n.Contains("plaza") || n.Contains("sentry")) return "wander_4000_stonewall.json";
            if (n.Contains("gate") || n.Contains("guard")) return "wander_4000_jam.json";
            if (n.Contains("park")) return "wander_4000_insect.json";
            if (n.Contains("quiet") || n.Contains("bus") || n.Contains("commuter")) return "wander_4000_water.json";
            if (n.Contains("rooftop") || n.Contains("runner")) return "wander_4000_dino.json";
            if (n.Contains("late") || n.Contains("alley")) return "wander_4000_zombie.json";
            if (n.Contains("night") || n.Contains("clerk")) return "wander_4000_machine.json";
            if (n.Contains("corner")) return "wander_4000_beatdown.json";
            return Street4000Decks[StableIndex(n + "|" + tearId, Street4000Decks.Length)];
        }

        static int StableIndex(string key, int len)
        {
            if (len <= 0) return 0;
            unchecked
            {
                var h = (key ?? "").GetHashCode();
                if (h == int.MinValue) h = 0;
                if (h < 0) h = -h;
                return h % len;
            }
        }

        public static ArDuelMatchConfig MakeStreetNpc(string tearId, string npcName, StreetLpBand band, bool digital)
        {
            var lp = band == StreetLpBand.Street8000
                ? MapZoneCatalog.Street8000Lp
                : MapZoneCatalog.Street4000Lp;
            var cfg = ArDuelMatchConfig.FromTear(tearId, npcName, ArDuelMatchConfig.DefaultStandM);
            cfg.Launch = ArDuelLaunchKind.NpcStreet;
            cfg.StartingLp = lp;
            cfg.PossessionCinematic = true;
            cfg.PreferDigital = digital;
            cfg.AiDeckFile = PickStreetDeck(npcName, tearId, band);
            cfg.FormatTitle = $"{npcName} · {lp} LP street duel";
            ApplyLastSurfaceScanIfAny(cfg);
            return cfg;
        }

        public static ArDuelMatchConfig MakeTearBoss(string zoneId, string zoneTitle, bool digital)
        {
            var cfg = MakeTearConfig(zoneId, zoneTitle, digital);
            cfg.Launch = ArDuelLaunchKind.TearBoss;
            cfg.StartingLp = MapZoneCatalog.TearBossLpFor(zoneId);
            cfg.TomeLegal = true;
            cfg.FormatTitle = $"Tear Boss · {cfg.StartingLp} LP · {(zoneTitle ?? "Rift")}";
            return cfg;
        }

        public static ArDuelMatchConfig MakeRaidBoss(string zoneId, string zoneTitle, bool digital,
            int partySize = 1, bool fillAlliesAi = false)
        {
            var cfg = MakeTearConfig(zoneId, zoneTitle, digital);
            cfg.Launch = ArDuelLaunchKind.RaidBoss;
            cfg.StartingLp = MapZoneCatalog.RaidBossLp;
            cfg.TomeLegal = true;
            cfg.RaidPartySize = Mathf.Clamp(partySize, 1, 3);
            cfg.RaidFillAlliesAi = fillAlliesAi && cfg.RaidPartySize > 1;
            var party = cfg.RaidPartySize <= 1
                ? "solo"
                : (cfg.RaidFillAlliesAi ? "3-on-1 · AI allies" : "3-on-1");
            cfg.FormatTitle = $"Raid · {cfg.StartingLp} LP · {party} · {(zoneTitle ?? "Boss")}";
            return cfg;
        }

        public static ArDuelMatchConfig MakeStoryEra(string zoneId, string zoneTitle, bool digital)
        {
            var cfg = MakeTearConfig(zoneId, zoneTitle, digital);
            cfg.Launch = ArDuelLaunchKind.StoryEra;
            cfg.StartingLp = 8000;
            cfg.FormatTitle = "Story · Umbrax fracture · " + (zoneTitle ?? "Mission");
            return cfg;
        }

        public static ArDuelMatchConfig MakePvpZone(string zoneTitle)
        {
            var c = ArDuelMatchConfig.PlayerVsPlayer(ArDuelMatchConfig.DefaultStandM);
            c.Launch = ArDuelLaunchKind.PvpZone;
            c.FormatTitle = string.IsNullOrEmpty(zoneTitle) ? "PvP Zone" : "PvP · " + zoneTitle;
            c.StartingLp = 8000;
            ApplyLastSurfaceScanIfAny(c);
            return c;
        }

        /// <summary>
        /// Reuse the most recent overworld surface scan (PvE/PvP) so zone duels
        /// inherit the same arena size without a second walk.
        /// </summary>
        public static void ApplyLastSurfaceScanIfAny(ArDuelMatchConfig cfg)
        {
            if (cfg == null) return;
            var d = PlayerPrefs.GetFloat("wrldz.surface.depthM", 0f);
            var w = PlayerPrefs.GetFloat("wrldz.surface.widthM", 0f);
            if (d < ArDuelMatchConfig.MinSeparationM) return;
            cfg.SeparationMeters = Mathf.Clamp(d, ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            cfg.SurfaceDepthM = cfg.SeparationMeters;
            cfg.SurfaceWidthM = w > 0.5f ? w : cfg.SeparationMeters * 0.92f;
            cfg.AutoSurfaceScan = true;
        }

        public static void PersistSurfaceScan(ArDuelMatchConfig cfg)
        {
            if (cfg == null || !cfg.AutoSurfaceScan) return;
            if (cfg.SurfaceDepthM < ArDuelMatchConfig.MinSeparationM &&
                cfg.SeparationMeters < ArDuelMatchConfig.MinSeparationM)
                return;
            var d = cfg.SurfaceDepthM > 0.5f ? cfg.SurfaceDepthM : cfg.SeparationMeters;
            var w = cfg.SurfaceWidthM > 0.5f ? cfg.SurfaceWidthM : d * 0.92f;
            PlayerPrefs.SetFloat("wrldz.surface.depthM", d);
            PlayerPrefs.SetFloat("wrldz.surface.widthM", w);
            PlayerPrefs.Save();
        }
    }
}
