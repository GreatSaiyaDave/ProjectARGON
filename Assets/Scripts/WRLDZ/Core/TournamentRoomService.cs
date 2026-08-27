using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Tournaments are rooms, not map pins. One player hosts; the match
    /// can start when enough duelists have joined (or the host fills seats).
    /// Local prototype of future networked lobbies.
    /// </summary>
    public static class TournamentRoomService
    {
        public const int MinSeats = 2;
        public const int MaxSeats = 8;
        public const int DefaultSeats = 4;

        public class Seat
        {
            public string displayName = "";
            public bool isHost;
            public bool isAi;
            public bool occupied;
        }

        public class Room
        {
            public string id = "";
            public string title = "";
            public string hostName = "";
            public int seatCount = DefaultSeats;
            public int minToStart = MinSeats;
            public bool started;
            public readonly List<Seat> seats = new();

            public int Occupied
            {
                get
                {
                    var n = 0;
                    for (var i = 0; i < seats.Count; i++)
                        if (seats[i].occupied) n++;
                    return n;
                }
            }

            public bool CanStart => !started && Occupied >= Mathf.Max(MinSeats, minToStart);
        }

        static readonly List<Room> Rooms = new();

        public static IReadOnlyList<Room> All => Rooms;

        public static Room HostedByLocal
        {
            get
            {
                var me = LocalName();
                for (var i = 0; i < Rooms.Count; i++)
                    if (string.Equals(Rooms[i].hostName, me, StringComparison.Ordinal))
                        return Rooms[i];
                return null;
            }
        }

        public static Room JoinedByLocal
        {
            get
            {
                var me = LocalName();
                for (var i = 0; i < Rooms.Count; i++)
                    if (IndexOfLocal(Rooms[i], me) >= 0)
                        return Rooms[i];
                return null;
            }
        }

        public static Room Host(string title, int seatCount)
        {
            Leave();
            var seats = Mathf.Clamp(seatCount, MinSeats, MaxSeats);
            var room = new Room
            {
                id = "t." + Guid.NewGuid().ToString("N").Substring(0, 8),
                title = string.IsNullOrWhiteSpace(title) ? "Open bracket" : title.Trim(),
                hostName = LocalName(),
                seatCount = seats,
                minToStart = Mathf.Max(MinSeats, seats / 2)
            };
            for (var i = 0; i < seats; i++)
                room.seats.Add(new Seat());
            room.seats[0].occupied = true;
            room.seats[0].isHost = true;
            room.seats[0].displayName = room.hostName;
            Rooms.Insert(0, room);
            Debug.Log($"[WRLDZ] Tournament hosted {room.id} · {room.title} · {seats} seats");
            return room;
        }

        public static bool TryJoin(string roomId, out string error)
        {
            error = null;
            var room = Find(roomId);
            if (room == null)
            {
                error = "Room closed.";
                return false;
            }

            if (room.started)
            {
                error = "Already underway.";
                return false;
            }

            Leave();
            var empty = FirstEmpty(room);
            if (empty < 0)
            {
                error = "Room full.";
                return false;
            }

            room.seats[empty].occupied = true;
            room.seats[empty].isHost = false;
            room.seats[empty].isAi = false;
            room.seats[empty].displayName = LocalName();
            return true;
        }

        public static void Leave()
        {
            var me = LocalName();
            for (var i = Rooms.Count - 1; i >= 0; i--)
            {
                var room = Rooms[i];
                var idx = IndexOfLocal(room, me);
                if (idx < 0) continue;
                if (room.seats[idx].isHost)
                {
                    Rooms.RemoveAt(i);
                    continue;
                }

                room.seats[idx] = new Seat();
            }
        }

        public static bool FillAi(string roomId, out string error)
        {
            error = null;
            var room = Find(roomId);
            if (room == null)
            {
                error = "Room closed.";
                return false;
            }

            if (room.started)
            {
                error = "Already underway.";
                return false;
            }

            var n = 1;
            for (var i = 0; i < room.seats.Count; i++)
            {
                if (room.seats[i].occupied) continue;
                room.seats[i].occupied = true;
                room.seats[i].isAi = true;
                room.seats[i].displayName = "AI " + n;
                n++;
            }

            return true;
        }

        public static bool TryStart(string roomId, out ArDuelMatchConfig cfg, out string error)
        {
            cfg = null;
            error = null;
            var room = Find(roomId);
            if (room == null)
            {
                error = "Room closed.";
                return false;
            }

            if (!room.CanStart)
            {
                error = $"Need {Mathf.Max(MinSeats, room.minToStart)} duelists to start.";
                return false;
            }

            room.started = true;
            cfg = ArDuelMatchConfig.DefaultQuick();
            cfg.Launch = ArDuelLaunchKind.Tournament;
            cfg.Opponent = ArDuelOpponentKind.AiLocal;
            cfg.FormatTitle = "Tournament · " + room.title;
            cfg.StartingLp = 8000;
            cfg.EntrySource = AppSession.SceneOverworld;
            cfg.ZoneId = room.id;
            cfg.ZoneTitle = room.title;
            MapZoneService.ApplyLastSurfaceScanIfAny(cfg);
            Debug.Log($"[WRLDZ] Tournament start {room.id} · {room.Occupied}/{room.seatCount}");
            return true;
        }

        public static Room Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < Rooms.Count; i++)
                if (Rooms[i].id == id) return Rooms[i];
            return null;
        }

        static int FirstEmpty(Room room)
        {
            for (var i = 0; i < room.seats.Count; i++)
                if (!room.seats[i].occupied) return i;
            return -1;
        }

        static int IndexOfLocal(Room room, string me)
        {
            for (var i = 0; i < room.seats.Count; i++)
            {
                var s = room.seats[i];
                if (s.occupied && !s.isAi && string.Equals(s.displayName, me, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        static string LocalName()
        {
            var acc = AppSession.Ensure()?.Account;
            if (acc != null && !string.IsNullOrEmpty(acc.displayName))
                return acc.displayName;
            if (acc != null && !string.IsNullOrEmpty(acc.username))
                return acc.username;
            return "Duelist";
        }
    }
}
