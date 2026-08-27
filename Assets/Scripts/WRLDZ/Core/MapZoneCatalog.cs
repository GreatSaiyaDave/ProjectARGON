using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Overworld zone kinds. Live map pins: Tear, Raid, Bazaar, Training, Story, PvP.
    /// Tournaments are NOT pins — they form wherever duelists gather, or remotely.
    /// NPCs wander around Tear spawn points (not static pins).
    /// Legacy kinds (Portal, Tournament, Event, Treasure, Anchor) still parse.
    /// </summary>
    public enum MapZoneKind
    {
        Tear = 0,
        Portal,
        Raid,
        Bazaar,
        Training,
        Tournament,
        Event,
        Npc,
        Treasure,
        Anchor,
        Story,
        Pvp
    }

    public enum MapZoneAction
    {
        ZoneModeDuel,
        OpenBazaar,
        OpenRaid,
        OpenStory,
        TrainingDuel,
        LootGrant,
        Toast,
        EventStub,
        NpcPossessDuel,
        OpenPvp
    }

    /// <summary>Street NPC LP band spawned around Tears.</summary>
    public enum StreetLpBand
    {
        Street4000 = 0,
        Street8000 = 1
    }

    public static class MapZoneCatalog
    {
        public const float DefaultInteractM = 55f;
        public const float TearNpcMaxRangeM = 200f;
        /// <summary>How far we look for other Tears when scoring ruralness.</summary>
        public const float RuralDensitySampleM = 1200f;
        public const float UrbanNpcRadiusM = 90f;
        public const float RuralNpcRadiusMMax = 800f;
        public const float StreetInteractM = 28f;
        public const int TearBossNeedStreak = 3;
        public const int Street4000Lp = 4000;
        public const int Street8000Lp = 8000;
        public const int TearBossLpMin = 10000;
        public const int TearBossLpMax = 15000;
        public const int RaidBossLp = 20000;
        public const long StreetAccessWindowSec = 24 * 60 * 60;

        public static MapZoneKind Parse(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return MapZoneKind.Tear;
            switch (kind.Trim().ToLowerInvariant())
            {
                case "tear": return MapZoneKind.Tear;
                case "portal": return MapZoneKind.Story;
                case "raid": return MapZoneKind.Raid;
                case "bazaar": return MapZoneKind.Bazaar;
                case "training": case "train": return MapZoneKind.Training;
                case "tournament": case "tourney": return MapZoneKind.Tournament;
                case "event": return MapZoneKind.Event;
                case "npc": case "street": return MapZoneKind.Npc;
                case "treasure": case "loot": return MapZoneKind.Treasure;
                case "anchor": case "leyline": case "shrine": return MapZoneKind.Anchor;
                case "story": return MapZoneKind.Story;
                case "pvp": case "versus": case "player": return MapZoneKind.Pvp;
                default: return MapZoneKind.Tear;
            }
        }

        public static string WireId(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => "tear",
            MapZoneKind.Raid => "raid",
            MapZoneKind.Bazaar => "bazaar",
            MapZoneKind.Training => "training",
            MapZoneKind.Story => "story",
            MapZoneKind.Pvp => "pvp",
            MapZoneKind.Npc => "npc",
            MapZoneKind.Portal => "story",
            MapZoneKind.Tournament => "tournament",
            MapZoneKind.Event => "event",
            MapZoneKind.Treasure => "treasure",
            MapZoneKind.Anchor => "anchor",
            _ => "tear"
        };

        /// <summary>Short HUD kind name.</summary>
        public static string Label(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => "TEAR ZONE",
            MapZoneKind.Raid => "RAID ZONE",
            MapZoneKind.Bazaar => "BAZAAR ZONE",
            MapZoneKind.Training => "TRAINING ZONE",
            MapZoneKind.Story => "STORY ZONE",
            MapZoneKind.Pvp => "PvP ZONE",
            MapZoneKind.Npc => "STREET DUELIST",
            MapZoneKind.Tournament => "TOURNAMENT",
            MapZoneKind.Portal => "STORY ZONE",
            MapZoneKind.Event => "EVENT",
            MapZoneKind.Treasure => "SHARD",
            MapZoneKind.Anchor => "LEYLINE",
            _ => "ZONE"
        };

        /// <summary>Title that states the zone's job on the map / nearby list.</summary>
        public static string PurposeTitle(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => "Rift stop · harvest · wanderers · Tear boss",
            MapZoneKind.Raid => "3-on-1 · 20k+ LP · Tome · solo OK",
            MapZoneKind.Bazaar => "Clothier, tailor, tablets, storage",
            MapZoneKind.Training => "Zero XP · Referobot or friends · no stakes",
            MapZoneKind.Story => "Umbrax fracture · LOB/MRD/SRL · set orbs to L50",
            MapZoneKind.Pvp => "Player vs player · PvP rewards",
            MapZoneKind.Npc => "Street encounter · spirit possession",
            MapZoneKind.Tournament => "Forms where duelists gather — not a pin",
            _ => ActionHint(k)
        };

        public static MapZoneAction Action(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => MapZoneAction.ZoneModeDuel,
            MapZoneKind.Raid => MapZoneAction.OpenRaid,
            MapZoneKind.Bazaar => MapZoneAction.OpenBazaar,
            MapZoneKind.Training => MapZoneAction.TrainingDuel,
            MapZoneKind.Story => MapZoneAction.OpenStory,
            MapZoneKind.Portal => MapZoneAction.OpenStory,
            MapZoneKind.Pvp => MapZoneAction.OpenPvp,
            MapZoneKind.Npc => MapZoneAction.NpcPossessDuel,
            MapZoneKind.Treasure => MapZoneAction.LootGrant,
            MapZoneKind.Anchor => MapZoneAction.LootGrant,
            MapZoneKind.Tournament => MapZoneAction.Toast,
            MapZoneKind.Event => MapZoneAction.Toast,
            _ => MapZoneAction.Toast
        };

        public static float InteractRadiusM(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => DefaultInteractM,
            MapZoneKind.Raid => DefaultInteractM,
            MapZoneKind.Bazaar => 40f,
            MapZoneKind.Training => DefaultInteractM,
            MapZoneKind.Story => 45f,
            MapZoneKind.Pvp => 45f,
            MapZoneKind.Npc => StreetInteractM,
            MapZoneKind.Portal => 45f,
            MapZoneKind.Tournament => DefaultInteractM,
            MapZoneKind.Event => 40f,
            MapZoneKind.Treasure => 30f,
            MapZoneKind.Anchor => 35f,
            _ => DefaultInteractM
        };

        public static Color Accent(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => new Color(1f, 0.55f, 0.95f, 1f),
            MapZoneKind.Raid => new Color(1f, 0.32f, 0.28f, 1f),
            MapZoneKind.Bazaar => new Color(1f, 0.82f, 0.25f, 1f),
            MapZoneKind.Training => new Color(0.45f, 0.95f, 0.65f, 1f),
            MapZoneKind.Story => new Color(0.85f, 0.70f, 1f, 1f),
            MapZoneKind.Pvp => new Color(0.35f, 0.85f, 1f, 1f),
            MapZoneKind.Npc => new Color(0.95f, 0.82f, 0.55f, 1f),
            MapZoneKind.Portal => new Color(0.85f, 0.70f, 1f, 1f),
            MapZoneKind.Tournament => new Color(1f, 0.88f, 0.35f, 1f),
            MapZoneKind.Event => new Color(1f, 0.92f, 0.45f, 1f),
            MapZoneKind.Treasure => new Color(1f, 0.85f, 0.35f, 1f),
            MapZoneKind.Anchor => new Color(0.7f, 0.9f, 1f, 1f),
            _ => Color.white
        };

        public static int RadarPriority(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => 0,
            MapZoneKind.Npc => 1,
            MapZoneKind.Raid => 2,
            MapZoneKind.Story => 3,
            MapZoneKind.Pvp => 4,
            MapZoneKind.Bazaar => 5,
            MapZoneKind.Training => 6,
            _ => 20
        };

        public static bool UsesZoneModeDuel(MapZoneKind k) =>
            Action(k) == MapZoneAction.ZoneModeDuel
            || Action(k) == MapZoneAction.TrainingDuel
            || Action(k) == MapZoneAction.NpcPossessDuel
            || Action(k) == MapZoneAction.OpenRaid
            || Action(k) == MapZoneAction.OpenPvp;

        public static bool ShowsRangeRing(MapZoneKind k) => false;

        public static bool IsLiveMapPin(MapZoneKind k) =>
            k == MapZoneKind.Tear || k == MapZoneKind.Raid || k == MapZoneKind.Bazaar
            || k == MapZoneKind.Training || k == MapZoneKind.Story || k == MapZoneKind.Pvp;

        public static float PinHalfHeight(MapZoneKind k) => k switch
        {
            MapZoneKind.Raid => 0.052f,
            MapZoneKind.Tear => 0.050f,
            MapZoneKind.Story => 0.046f,
            MapZoneKind.Pvp => 0.046f,
            MapZoneKind.Bazaar => 0.044f,
            MapZoneKind.Training => 0.042f,
            MapZoneKind.Npc => 0.034f,
            _ => 0.038f
        };

        public static string Glyph(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => "⚡",
            MapZoneKind.Raid => "♜",
            MapZoneKind.Bazaar => "✦",
            MapZoneKind.Training => "†",
            MapZoneKind.Story => "§",
            MapZoneKind.Pvp => "⚔",
            MapZoneKind.Npc => "☺",
            _ => "•"
        };

        public static string ActionHint(MapZoneKind k) => k switch
        {
            MapZoneKind.Tear => "Rift",
            MapZoneKind.Raid => "Raid boss",
            MapZoneKind.Bazaar => "Trade",
            MapZoneKind.Training => "Practice",
            MapZoneKind.Story => "Mission",
            MapZoneKind.Pvp => "PvP",
            MapZoneKind.Npc => "Duel",
            MapZoneKind.Tournament => "Gather",
            _ => "Zone"
        };

        /// <summary>
        /// NPC wander/spawn radius around a Tear. Fewer nearby Tears (rural) → larger radius
        /// so players in the middle of nowhere still find street duelists.
        /// </summary>
        public static float RuralNpcRadiusM(int nearbyTearCount)
        {
            return nearbyTearCount switch
            {
                0 => RuralNpcRadiusMMax,
                1 => 520f,
                2 => 320f,
                3 => 180f,
                _ => UrbanNpcRadiusM
            };
        }

        /// <summary>How many street NPCs a Tear should show given player distance in meters.</summary>
        public static int StreetSpawnBudget(float distToTearM, float ruralRadiusM = 0f)
        {
            var range = Mathf.Max(TearNpcMaxRangeM, ruralRadiusM);
            if (distToTearM > range) return 0;
            var t = range > 1f ? distToTearM / range : 1f;
            if (t > 0.70f) return 2;
            if (t > 0.45f) return 4;
            if (t > 0.25f) return 7;
            return 10;
        }

        /// <summary>Share of spawned NPCs that are 8000 LP (rest 4000). Closer = harder.</summary>
        public static float StreetHardShare(float distToTearM)
        {
            if (distToTearM > 120f) return 0.15f;
            if (distToTearM > 70f) return 0.35f;
            if (distToTearM > 40f) return 0.55f;
            return 0.75f;
        }

        public static int TearBossLpFor(string zoneId)
        {
            var h = Mathf.Abs((zoneId ?? "tear").GetHashCode());
            return TearBossLpMin + h % (TearBossLpMax - TearBossLpMin + 1);
        }
    }
}
