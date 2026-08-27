using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Snapshot of hologram-relevant duel field state for multiplayer sync.
    /// Both peers apply the same snapshot so arena + disk representations match.
    /// </summary>
    [Serializable]
    public class ArHologramSnapshot
    {
        public int TurnNumber;
        public string Phase;
        public int PlayerLp;
        public int OppLp;
        public List<ArZoneCardSnap> PlayerMonsters = new();
        public List<ArZoneCardSnap> OppMonsters = new();
        public List<ArZoneCardSnap> PlayerSpellTraps = new();
        public List<ArZoneCardSnap> OppSpellTraps = new();
        public ArZoneCardSnap PlayerField;
        public ArZoneCardSnap OppField;
        public ArZoneCardSnap PlayerPendL;
        public ArZoneCardSnap PlayerPendR;
        public ArZoneCardSnap OppPendL;
        public ArZoneCardSnap OppPendR;
        public long TimestampMs;
    }

    [Serializable]
    public class ArZoneCardSnap
    {
        public int ZoneIndex;
        public int InstanceId;
        public int CardId;
        public bool FaceUp;
        public bool Defense;
        public int Counters;
    }

    /// <summary>Abstraction over local / networked hologram state distribution.</summary>
    public interface IArDuelSync
    {
        event Action<ArHologramSnapshot> OnRemoteSnapshot;
        void Publish(ArHologramSnapshot snap);
        void Tick();
    }

    /// <summary>
    /// Local dual-view sync: immediately echoes snapshots to all local subscribers.
    /// Replace with Netcode/Mirror transport later without changing arena/disk code.
    /// </summary>
    public class LocalArDuelSync : IArDuelSync
    {
        public event Action<ArHologramSnapshot> OnRemoteSnapshot;
        ArHologramSnapshot _last;

        public void Publish(ArHologramSnapshot snap)
        {
            _last = snap;
            // Local multiplayer / split-view: treat publish as authoritative for all listeners
            OnRemoteSnapshot?.Invoke(snap);
        }

        public void Tick() { /* no network pump */ }

        public ArHologramSnapshot Last => _last;
    }

    /// <summary>
    /// Builds snapshots from <see cref="DuelEngine"/> and drives arena/disk via sync.
    /// </summary>
    public class ArDuelSyncBridge : MonoBehaviour
    {
        public IArDuelSync Sync = new LocalArDuelSync();
        public ArArenaHologramManager Arena;
        public ArCardFieldController CardField;
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;

        DuelEngine _engine;
        WRLDZ.Data.CardDatabase _db;
        int _lastHash;

        public void Bind(DuelEngine engine, WRLDZ.Data.CardDatabase db)
        {
            // Avoid double-subscribe (DuelUI already syncs AR via SyncNow each refresh)
            if (ReferenceEquals(_engine, engine) && ReferenceEquals(_db, db))
            {
                Push();
                return;
            }

            if (_engine != null)
                _engine.OnStateChanged -= Push;
            _engine = engine;
            _db = db;
            // Do not subscribe OnStateChanged here — DuelUI.RefreshNow → SyncNow is the single path.
            // Prevents double arena rebuild fighting the UI field rebuild.
            Push();
        }

        public void Unbind()
        {
            if (_engine != null)
                _engine.OnStateChanged -= Push;
            _engine = null;
        }

        void OnDestroy() => Unbind();

        void Push()
        {
            if (_engine?.Player == null || _engine.Opponent == null) return;
            var snap = Capture(_engine);
            var hash = snap.GetHashCode() ^ snap.PlayerLp ^ snap.OppLp ^ snap.TurnNumber;
            // Always publish — engine is authority; listeners refresh holos
            Sync?.Publish(snap);
            ApplyLocal(snap);
            _lastHash = hash;
            // Full rules snapshot for multiplayer peers (identical legal state)
            _ = _engine.CaptureRulesState();
        }

        void ApplyLocal(ArHologramSnapshot snap)
        {
            // Card Field (disk) + Hologram Field (arena) from live engine authority
            if (_db != null)
            {
                CardField?.SyncFromEngine(_engine, _db);
                Arena?.SyncFromEngine(_engine, _db);
            }
            else
            {
                PlayerDisk?.SyncOccupantsFrom(_engine.Player);
                OppDisk?.SyncOccupantsFrom(_engine.Opponent);
            }
        }

        public static ArHologramSnapshot Capture(DuelEngine engine)
        {
            var s = new ArHologramSnapshot
            {
                TurnNumber = engine.TurnNumber,
                Phase = engine.Phase.ToString(),
                PlayerLp = engine.Player.LifePoints,
                OppLp = engine.Opponent.LifePoints,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            PackMonsters(engine.Player, s.PlayerMonsters);
            PackMonsters(engine.Opponent, s.OppMonsters);
            PackSt(engine.Player, s.PlayerSpellTraps);
            PackSt(engine.Opponent, s.OppSpellTraps);
            s.PlayerField = PackOne(engine.Player.FieldSpellZone?.Occupant, 0);
            s.OppField = PackOne(engine.Opponent.FieldSpellZone?.Occupant, 0);
            s.PlayerPendL = PackOne(engine.Player.PendulumZones?[0]?.Occupant, 0);
            s.PlayerPendR = PackOne(engine.Player.PendulumZones?[1]?.Occupant, 1);
            s.OppPendL = PackOne(engine.Opponent.PendulumZones?[0]?.Occupant, 0);
            s.OppPendR = PackOne(engine.Opponent.PendulumZones?[1]?.Occupant, 1);
            return s;
        }

        static void PackMonsters(DuelistState who, List<ArZoneCardSnap> list)
        {
            list.Clear();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var c = who.MonsterZones[i].Occupant;
                if (c == null) continue;
                list.Add(PackOne(c, i));
            }
        }

        static void PackSt(DuelistState who, List<ArZoneCardSnap> list)
        {
            list.Clear();
            for (var i = 0; i < who.SpellTrapZones.Length; i++)
            {
                var c = who.SpellTrapZones[i].Occupant;
                if (c == null) continue;
                list.Add(PackOne(c, i));
            }
        }

        static ArZoneCardSnap PackOne(CardInstance c, int zone)
        {
            if (c == null) return null;
            return new ArZoneCardSnap
            {
                ZoneIndex = zone,
                InstanceId = c.InstanceId,
                CardId = c.CardId,
                FaceUp = c.FaceUp,
                Defense = c.Position == BattlePosition.Defense,
                Counters = 0
            };
        }
    }
}
