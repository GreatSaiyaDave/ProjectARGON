using System;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>How an AR duel was launched (telemetry + return UX).</summary>
    public enum ArDuelLaunchKind
    {
        /// <summary>Overworld CREATE DUEL menu.</summary>
        CreateMenu = 0,
        /// <summary>Tear pin within interact radius.</summary>
        TearZone = 1,
        /// <summary>Hub / format select.</summary>
        Hub = 2,
        /// <summary>Practice orb (anywhere).</summary>
        Practice = 3,
        /// <summary>Boot lab TEST DUEL.</summary>
        LabTest = 4,
        /// <summary>Player vs Player with GPS / walk distance scan.</summary>
        NearbyChallenge = 5,
        /// <summary>Wandering Tear-adjacent street NPC (possession beat).</summary>
        NpcStreet = 6,
        /// <summary>Tear Zone boss (10–15k LP) after 3 straight 8000 street wins.</summary>
        TearBoss = 7,
        /// <summary>Raid Zone boss (20k+ LP, Tome legal, 3-on-1).</summary>
        RaidBoss = 8,
        /// <summary>Story Zone era mission.</summary>
        StoryEra = 9,
        /// <summary>PvP Zone ranked rewards.</summary>
        PvpZone = 10,
        /// <summary>Hosted tournament room (starts when enough join).</summary>
        Tournament = 11
    }

    /// <summary>Who you face in the AR field.</summary>
    public enum ArDuelOpponentKind
    {
        AiLocal = 0,
        /// <summary>Local hotseat / shared device (same phone) — both humans.</summary>
        LocalPass = 1,
        /// <summary>Face-to-face PvP; distance from dual GPS marks (or walk measure).</summary>
        NearbyPeer = 2
    }

    /// <summary>
    /// AR duel session parameters. All live duels run in AR and the field
    /// is anchored using the separation between duelists (meters).
    /// Pure data — no scene / engine coupling.
    /// </summary>
    [Serializable]
    public class ArDuelMatchConfig
    {
        public ArDuelLaunchKind Launch = ArDuelLaunchKind.CreateMenu;
        public ArDuelOpponentKind Opponent = ArDuelOpponentKind.AiLocal;
        public string FormatId = "quick";
        public string FormatTitle = "AR Quick Duel";
        public string EntrySource = AppSession.SceneOverworld;

        /// <summary>ERAZ TCG band id for this match (default Original).</summary>
        public string ErazBandId = ErazFormat.Original;

        /// <summary>
        /// Real-world meters between you and the opponent.
        /// Arena midpoint sits between both arms; disks scale to this distance.
        /// </summary>
        public float SeparationMeters = 2.5f;

        /// <summary>
        /// True when SeparationMeters came from <see cref="ArenaSurfaceScanner"/>
        /// (auto walk / GPS surface measure) rather than a fixed preset.
        /// </summary>
        public bool AutoSurfaceScan;

        /// <summary>Measured play-surface depth (near→far), meters. 0 = use Separation only.</summary>
        public float SurfaceDepthM;

        /// <summary>Estimated play-surface width, meters (from scan proportion).</summary>
        public float SurfaceWidthM;

        /// <summary>Optional tear / arena pin id when launched from overworld pin.</summary>
        public string ZoneId = "";
        public string ZoneTitle = "";

        /// <summary>Host GPS at create time (for multiplayer handoff later).</summary>
        public double HostLatitude;
        public double HostLongitude;
        public bool HostGpsValid;

        /// <summary>Guest GPS when known (NearbyChallenge); otherwise empty.</summary>
        public double GuestLatitude;
        public double GuestLongitude;
        public bool GuestGpsValid;

        public string PlayerDeckFile = "";
        public string AiDeckFile = "";

        /// <summary>
        /// F2P decline path: same DuelEngine + spatial stage, no camera passthrough.
        /// Set by Zone Mode prompt [DIGITAL].
        /// </summary>
        public bool PreferDigital;

        /// <summary>
        /// When true, skip disk deploy / shuffle / DRAW HAND and start with opening hands.
        /// Set by Desktop Lab "Instant Duel" or skip-cinematic option.
        /// </summary>
        public bool SkipPreDuelCinematic;

        /// <summary>0 = use TCG default 8000. Street 4000 / 8000, Tear boss 10–15k, Raid 20k+.</summary>
        public int StartingLp;

        /// <summary>Story catalog stage id when <see cref="Launch"/> is StoryEra.</summary>
        public string StoryStageId = "";

        /// <summary>Duelist Kingdom table overlay (2000 LP, no direct attacks).</summary>
        public bool DkOverlay;

        public const int DuelistKingdomStartingLp = 2000;

        /// <summary>Tome pages legal (Tear boss + Raid only).</summary>
        public bool TomeLegal;

        /// <summary>AR: NPC looks human, then a Tear opens and a spirit possesses them.</summary>
        public bool PossessionCinematic;

        /// <summary>Raid seats filled (1 = solo, 3 = 3-on-1). Engine is still 1v1 vs the boss.</summary>
        public int RaidPartySize = 1;

        /// <summary>Empty raid seats filled with AI allies (placeholder until networked 3-on-1).</summary>
        public bool RaidFillAlliesAi;

        public const float MinSeparationM = 1.2f;
        public const float MaxSeparationM = 8f;
        public const float DefaultTableM = 1.6f;
        public const float DefaultStandM = 2.5f;
        public const float DefaultStreetM = 4.0f;

        public static ArDuelMatchConfig DefaultQuick() => new()
        {
            Launch = ArDuelLaunchKind.CreateMenu,
            Opponent = ArDuelOpponentKind.AiLocal,
            FormatId = "quick",
            FormatTitle = "AR Quick Duel",
            SeparationMeters = DefaultStandM,
            EntrySource = AppSession.SceneOverworld
        };

        public static ArDuelMatchConfig FromTear(string zoneId, string zoneTitle, float separationM = DefaultStandM)
        {
            var c = DefaultQuick();
            c.Launch = ArDuelLaunchKind.TearZone;
            c.ZoneId = zoneId ?? "";
            c.ZoneTitle = zoneTitle ?? "Tear";
            c.FormatTitle = "AR Zone Duel · " + c.ZoneTitle;
            c.SeparationMeters = Mathf.Clamp(separationM, MinSeparationM, MaxSeparationM);
            return c;
        }

        public static ArDuelMatchConfig Practice() => new()
        {
            Launch = ArDuelLaunchKind.Practice,
            Opponent = ArDuelOpponentKind.AiLocal,
            FormatId = "practice",
            FormatTitle = "AR Practice Duel",
            SeparationMeters = DefaultStandM,
            EntrySource = AppSession.SceneOverworld
        };

        /// <summary>Player vs Player after distance scan (host + guest marks).</summary>
        public static ArDuelMatchConfig PlayerVsPlayer(float separationM)
        {
            var c = new ArDuelMatchConfig
            {
                Launch = ArDuelLaunchKind.NearbyChallenge,
                Opponent = ArDuelOpponentKind.NearbyPeer,
                FormatId = "pvp",
                FormatTitle = "Player vs Player",
                SeparationMeters = Mathf.Clamp(separationM, MinSeparationM, MaxSeparationM),
                EntrySource = AppSession.SceneOverworld
            };
            return c;
        }

        /// <summary>True when both sides are humans (hotseat / nearby peer).</summary>
        public bool IsHumanOpponent =>
            Opponent == ArDuelOpponentKind.LocalPass || Opponent == ArDuelOpponentKind.NearbyPeer;

        public static bool IsStoryLaunch(ArDuelLaunchKind launch) =>
            launch == ArDuelLaunchKind.StoryEra;

        /// <summary>
        /// Hub PvAI Duelist Kingdom table. Story must never call this.
        /// Callers still gate with <see cref="FormatProgress.CanOptInDuelistKingdom"/>.
        /// </summary>
        public static ArDuelMatchConfig DuelistKingdomPvAi(float meters = DefaultStandM)
        {
            var c = DefaultQuick();
            c.Launch = ArDuelLaunchKind.Hub;
            c.Opponent = ArDuelOpponentKind.AiLocal;
            c.FormatId = FormatProgress.DuelistKingdomId;
            c.FormatTitle = "Duelist Kingdom · 2000 LP";
            c.DkOverlay = true;
            c.StartingLp = DuelistKingdomStartingLp;
            c.EntrySource = AppSession.SceneMainMenu;
            c.SeparationMeters = meters;
            c.ClampSeparation();
            return c;
        }

        /// <summary>
        /// Badge-gated DK overlay for a non-story config. StoryEra always returns false.
        /// </summary>
        public static bool TryApplyDuelistKingdomOptIn(ArDuelMatchConfig cfg, PlayerProgress progress)
        {
            if (cfg == null || progress == null) return false;
            if (IsStoryLaunch(cfg.Launch)) return false;
            if (!FormatProgress.CanOptInDuelistKingdom(progress)) return false;
            cfg.DkOverlay = true;
            cfg.StartingLp = DuelistKingdomStartingLp;
            cfg.FormatId = FormatProgress.DuelistKingdomId;
            if (string.IsNullOrEmpty(cfg.FormatTitle) ||
                cfg.FormatTitle.StartsWith("Player vs AI", StringComparison.OrdinalIgnoreCase))
                cfg.FormatTitle = "Duelist Kingdom · 2000 LP";
            return true;
        }

        /// <summary>Capture host GPS from <see cref="MapEnvironment"/> when available.</summary>
        public void CaptureHostLocation(MapEnvironment env)
        {
            if (env == null) return;
            if (env.UsingRealGps || env.GpsActive)
            {
                HostLatitude = env.Latitude;
                HostLongitude = env.Longitude;
                HostGpsValid = true;
            }
            else if (env.OriginSet)
            {
                // Simulated map origin as host mark
                HostLatitude = env.Latitude;
                HostLongitude = env.Longitude;
                HostGpsValid = true;
            }
        }

        /// <summary>Capture guest/player-2 mark from current GPS or map walk position.</summary>
        public void CaptureGuestLocation(MapEnvironment env)
        {
            if (env == null) return;
            if (env.UsingRealGps || env.GpsActive)
            {
                GuestLatitude = env.Latitude;
                GuestLongitude = env.Longitude;
                GuestGpsValid = true;
            }
            else
            {
                // Editor / pad: convert meters N/E from origin into a synthetic lat/lon offset
                // ~111_320 m per degree latitude
                const double mPerDegLat = 111320.0;
                var lat = env.OriginLatitude + env.MetersNorth / mPerDegLat;
                var cos = Math.Cos(env.OriginLatitude * Math.PI / 180.0);
                var mPerDegLon = mPerDegLat * Math.Max(0.2, Math.Abs(cos));
                var lon = env.OriginLongitude + env.MetersEast / mPerDegLon;
                GuestLatitude = lat;
                GuestLongitude = lon;
                GuestGpsValid = true;
                if (!HostGpsValid)
                {
                    HostLatitude = env.OriginLatitude;
                    HostLongitude = env.OriginLongitude;
                    HostGpsValid = true;
                }
            }

            RecomputeSeparationFromGps();
        }

        /// <summary>
        /// When both host and guest GPS are valid, recompute SeparationMeters
        /// from real-world haversine distance (PvP path).
        /// </summary>
        public void RecomputeSeparationFromGps()
        {
            if (!HostGpsValid || !GuestGpsValid) return;
            var raw = HaversineMeters(HostLatitude, HostLongitude, GuestLatitude, GuestLongitude);
            // Floor tiny GPS noise so arena never collapses below min
            if (raw < 0.4f) raw = MinSeparationM;
            SeparationMeters = Mathf.Clamp(raw, MinSeparationM, MaxSeparationM);
        }

        public static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0;
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (float)(R * c);
        }

        public void ClampSeparation()
        {
            SeparationMeters = Mathf.Clamp(SeparationMeters, MinSeparationM, MaxSeparationM);
        }

        public string SummaryLine()
        {
            var vs = Opponent switch
            {
                ArDuelOpponentKind.AiLocal => "vs AI",
                ArDuelOpponentKind.LocalPass => "hotseat PvP",
                ArDuelOpponentKind.NearbyPeer => "Player vs Player",
                _ => "duel"
            };
            var mode = PreferDigital ? "digital" : "AR";
            var scan = AutoSurfaceScan ? "scan" : "set";
            var lp = StartingLp > 0 ? $" · {StartingLp} LP" : "";
            return $"{FormatTitle} · {vs} · {SeparationMeters:0.0}m ({scan}) · {mode}{lp}";
        }
    }
}

