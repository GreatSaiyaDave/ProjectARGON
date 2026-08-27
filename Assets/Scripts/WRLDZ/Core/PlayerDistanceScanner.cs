using System;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Scans / marks two player positions and computes real-world separation for AR arena layout.
    /// Works with live GPS outdoors, or walk-pad / WASD meters in Editor / indoors.
    /// </summary>
    public class PlayerDistanceScanner
    {
        public enum Phase
        {
            Idle = 0,
            NeedHostMark,
            NeedGuestMark,
            Ready
        }

        public Phase State { get; private set; } = Phase.Idle;
        public ArDuelMatchConfig Draft { get; private set; } = ArDuelMatchConfig.PlayerVsPlayer(ArDuelMatchConfig.DefaultStandM);

        public bool HostMarked => Draft != null && Draft.HostGpsValid;
        public bool GuestMarked => Draft != null && Draft.GuestGpsValid;
        public float MeasuredMeters => Draft?.SeparationMeters ?? 0f;
        public string StatusLine { get; private set; } = "Ready to scan";

        public event Action OnChanged;

        public void Begin()
        {
            Draft = ArDuelMatchConfig.PlayerVsPlayer(ArDuelMatchConfig.DefaultStandM);
            Draft.Launch = ArDuelLaunchKind.NearbyChallenge;
            Draft.Opponent = ArDuelOpponentKind.NearbyPeer;
            Draft.FormatTitle = "Player vs Player";
            Draft.FormatId = "pvp";
            State = Phase.NeedHostMark;
            StatusLine = "Step 1 · Player 1 stands still · mark your position";
            Raise();
        }

        public void Reset()
        {
            Begin();
        }

        /// <summary>Mark Player 1 (host) at current GPS / map origin.</summary>
        public bool MarkHost()
        {
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null)
            {
                StatusLine = "Map environment missing";
                Raise();
                return false;
            }

            // Prefer locking origin at host so guest walk meters are relative
            if (!env.UsingRealGps)
                env.RecenterOriginHere();

            Draft.CaptureHostLocation(env);
            if (!Draft.HostGpsValid)
            {
                // Force host mark from origin even without GPS numbers
                Draft.HostLatitude = env.OriginLatitude;
                Draft.HostLongitude = env.OriginLongitude;
                Draft.HostGpsValid = true;
            }

            State = Phase.NeedGuestMark;
            StatusLine = env.UsingRealGps
                ? "Step 2 · Player 2 stands at their spot · mark opponent"
                : "Step 2 · Walk to Player 2 (WASD/pad) · mark opponent";
            Raise();
            return true;
        }

        /// <summary>Mark Player 2 (guest); auto-computes separation meters.</summary>
        public bool MarkGuest()
        {
            if (!HostMarked)
            {
                StatusLine = "Mark Player 1 first";
                Raise();
                return false;
            }

            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            Draft.CaptureGuestLocation(env);
            Draft.ClampSeparation();
            State = Phase.Ready;
            StatusLine =
                $"Distance locked · {Draft.SeparationMeters:0.0}m · arena will match this field";
            Debug.Log($"[WRLDZ PvP] Measured separation {Draft.SeparationMeters:0.00}m " +
                      $"(host GPS={Draft.HostGpsValid} guest GPS={Draft.GuestGpsValid})");
            Raise();
            return true;
        }

        /// <summary>Live preview distance while waiting for guest mark (walk meters or GPS).</summary>
        public float PreviewDistanceMeters()
        {
            if (!HostMarked || Draft == null) return 0f;
            var env = MapEnvironment.Instance;
            if (env == null) return 0f;

            if (env.UsingRealGps && Draft.HostGpsValid)
            {
                return ArDuelMatchConfig.HaversineMeters(
                    Draft.HostLatitude, Draft.HostLongitude,
                    env.Latitude, env.Longitude);
            }

            // Simulated: distance from map origin (host) to current walk offset
            return Mathf.Sqrt(env.MetersEast * env.MetersEast + env.MetersNorth * env.MetersNorth);
        }

        public string LiveScanLine()
        {
            if (State == Phase.NeedGuestMark)
            {
                var d = PreviewDistanceMeters();
                return $"Scanning… {d:0.0}m from Player 1";
            }

            if (State == Phase.Ready)
                return $"Ready · {MeasuredMeters:0.0}m field";
            return StatusLine;
        }

        /// <summary>Build final match config for <see cref="AppSession.StartArDuel"/>.</summary>
        public ArDuelMatchConfig BuildMatchConfig()
        {
            if (Draft == null) Begin();
            if (!GuestMarked)
            {
                // Fallback standing distance if user skips measure
                Draft.SeparationMeters = ArDuelMatchConfig.DefaultStandM;
                Draft.ClampSeparation();
            }
            else
            {
                Draft.RecomputeSeparationFromGps();
                Draft.ClampSeparation();
            }

            Draft.Opponent = ArDuelOpponentKind.NearbyPeer;
            Draft.Launch = ArDuelLaunchKind.NearbyChallenge;
            Draft.FormatId = "pvp";
            Draft.FormatTitle = "Player vs Player";
            Draft.EntrySource = AppSession.SceneOverworld;
            return Draft;
        }

        void Raise() => OnChanged?.Invoke();
    }
}
