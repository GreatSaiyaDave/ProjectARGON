using System;
using UnityEngine;

namespace WRLDZ.Core
{
    /// <summary>
    /// Auto surface / field scan for duel arena size — shared by PvE and PvP.
    ///
    /// Flow:
    /// 1. Mark "near end" (you / Player 1) — auto on <see cref="Begin"/>.
    /// 2. Walk to far end of table / floor / stand-off (GPS outdoors, pad/WASD indoors).
    /// 3. Auto-lock when you stop moving after a real span, or manual confirm.
    ///
    /// Result → <see cref="ArDuelMatchConfig.SeparationMeters"/> sizes disks + midfield.
    /// No AR Foundation required; lenses/phone can refine later via XR plane hits.
    /// </summary>
    public class ArenaSurfaceScanner
    {
        public enum Phase
        {
            Idle = 0,
            /// <summary>Host mark set; walking the surface.</summary>
            Scanning,
            /// <summary>Distance locked; ready to start duel.</summary>
            Ready
        }

        public enum ScanMode
        {
            /// <summary>You vs AI — walk the length of your play surface.</summary>
            Pve = 0,
            /// <summary>Two humans — walk from P1 to P2 stand points.</summary>
            Pvp = 1
        }

        public Phase State { get; private set; } = Phase.Idle;
        public ScanMode Mode { get; private set; } = ScanMode.Pve;
        public ArDuelMatchConfig Draft { get; private set; }

        /// <summary>Live meters from host mark to current position.</summary>
        public float LiveMeters { get; private set; }

        /// <summary>Locked span used as arena separation.</summary>
        public float SurfaceDepthM { get; private set; }

        /// <summary>Estimated width (playmat proportion of depth).</summary>
        public float SurfaceWidthM { get; private set; }

        public string StatusLine { get; private set; } = "";
        public event Action OnChanged;

        // Auto-lock: must exceed this span, then stillness
        const float MinScanSpanM = 1.25f;
        const float StillSpeedMps = 0.12f;
        const float StillHoldSec = 1.15f;
        const float MoveArmSec = 0.35f;

        float _stillT;
        float _movedT;
        float _lastPreview;
        float _lastSampleTime;
        Vector2 _lastEn;
        bool _hostOk;

        public void Begin(ScanMode mode)
        {
            Mode = mode;
            Draft = mode == ScanMode.Pvp
                ? ArDuelMatchConfig.PlayerVsPlayer(ArDuelMatchConfig.DefaultStandM)
                : ArDuelMatchConfig.DefaultQuick();
            Draft.Opponent = mode == ScanMode.Pvp
                ? ArDuelOpponentKind.NearbyPeer
                : ArDuelOpponentKind.AiLocal;
            Draft.Launch = mode == ScanMode.Pvp
                ? ArDuelLaunchKind.NearbyChallenge
                : ArDuelLaunchKind.CreateMenu;
            Draft.FormatId = mode == ScanMode.Pvp ? "pvp" : "pvai";
            Draft.FormatTitle = mode == ScanMode.Pvp ? "Player vs Player" : "Player vs AI";
            Draft.EntrySource = AppSession.SceneOverworld;
            Draft.AutoSurfaceScan = true;

            LiveMeters = 0f;
            SurfaceDepthM = 0f;
            SurfaceWidthM = 0f;
            _stillT = 0f;
            _movedT = 0f;
            _lastPreview = 0f;
            _lastSampleTime = Time.unscaledTime;
            _hostOk = false;

            // Auto mark near end immediately
            if (!TryMarkHostInternal())
            {
                State = Phase.Idle;
                StatusLine = "Map unavailable — cannot scan surface";
                Raise();
                return;
            }

            State = Phase.Scanning;
            StatusLine = mode == ScanMode.Pvp
                ? "Surface scan · walk to Player 2 stand point · stop to lock"
                : "Surface scan · walk the length of your table/floor · stop to lock";
            Raise();
        }

        public void Reset() => Begin(Mode);

        /// <summary>Manual lock at current distance (button).</summary>
        public bool ConfirmHere()
        {
            if (State != Phase.Scanning || !_hostOk) return false;
            return LockAt(LiveMeters > 0.05f ? LiveMeters : PreviewDistanceMeters());
        }

        /// <summary>Call every frame / 0.2s while UI is open.</summary>
        public void Tick(float dt)
        {
            if (State != Phase.Scanning || !_hostOk) return;
            dt = Mathf.Max(0.01f, dt);

            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null) return;

            var preview = PreviewDistanceMeters();
            LiveMeters = preview;

            // Horizontal speed estimate
            var en = new Vector2(env.MetersEast, env.MetersNorth);
            var now = Time.unscaledTime;
            var dT = Mathf.Max(0.05f, now - _lastSampleTime);
            var speed = (en - _lastEn).magnitude / dT;
            _lastEn = en;
            _lastSampleTime = now;

            var delta = Mathf.Abs(preview - _lastPreview);
            _lastPreview = preview;

            if (preview >= MinScanSpanM && (speed > StillSpeedMps || delta > 0.08f))
                _movedT += dt;
            else if (preview < MinScanSpanM * 0.5f)
                _movedT = 0f;

            // Auto-lock: walked far enough, then stood still
            if (preview >= MinScanSpanM && _movedT >= MoveArmSec)
            {
                if (speed < StillSpeedMps)
                {
                    _stillT += dt;
                    if (_stillT >= StillHoldSec)
                    {
                        LockAt(preview);
                        return;
                    }

                    StatusLine = Mode == ScanMode.Pvp
                        ? $"Hold still… locking PvP field · {preview:0.0}m"
                        : $"Hold still… locking surface · {preview:0.0}m";
                }
                else
                {
                    _stillT = 0f;
                    StatusLine = Mode == ScanMode.Pvp
                        ? $"Scanning to Player 2 · {preview:0.0}m"
                        : $"Scanning surface · {preview:0.0}m · walk to far edge";
                }
            }
            else
            {
                _stillT = 0f;
                StatusLine = Mode == ScanMode.Pvp
                    ? $"Walk toward Player 2 · {preview:0.0}m (min {MinScanSpanM:0.0}m)"
                    : $"Walk across play surface · {preview:0.0}m (min {MinScanSpanM:0.0}m)";
            }

            Raise();
        }

        bool TryMarkHostInternal()
        {
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null) return false;

            if (!env.UsingRealGps)
                env.RecenterOriginHere();

            Draft.CaptureHostLocation(env);
            if (!Draft.HostGpsValid)
            {
                Draft.HostLatitude = env.OriginLatitude;
                Draft.HostLongitude = env.OriginLongitude;
                Draft.HostGpsValid = true;
            }

            _hostOk = Draft.HostGpsValid;
            _lastEn = new Vector2(env.MetersEast, env.MetersNorth);
            _lastSampleTime = Time.unscaledTime;
            return _hostOk;
        }

        public float PreviewDistanceMeters()
        {
            if (!_hostOk || Draft == null) return 0f;
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env == null) return 0f;

            if (env.UsingRealGps && Draft.HostGpsValid)
            {
                return ArDuelMatchConfig.HaversineMeters(
                    Draft.HostLatitude, Draft.HostLongitude,
                    env.Latitude, env.Longitude);
            }

            return Mathf.Sqrt(env.MetersEast * env.MetersEast + env.MetersNorth * env.MetersNorth);
        }

        bool LockAt(float meters)
        {
            var env = MapEnvironment.Instance ?? MapEnvironment.Ensure();
            if (env != null)
                Draft.CaptureGuestLocation(env);

            // Prefer measured walk/GPS; clamp to arena limits
            if (meters < 0.4f) meters = ArDuelMatchConfig.DefaultStandM;
            SurfaceDepthM = Mathf.Clamp(meters, ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            // Playmat is slightly wider than deep for dual-disk stance
            SurfaceWidthM = Mathf.Clamp(SurfaceDepthM * 0.92f, ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);

            Draft.SeparationMeters = SurfaceDepthM;
            Draft.SurfaceDepthM = SurfaceDepthM;
            Draft.SurfaceWidthM = SurfaceWidthM;
            Draft.AutoSurfaceScan = true;
            Draft.ClampSeparation();

            LiveMeters = SurfaceDepthM;
            State = Phase.Ready;
            StatusLine = Mode == ScanMode.Pvp
                ? $"PvP field locked · {SurfaceDepthM:0.0}m × ~{SurfaceWidthM:0.0}m"
                : $"Surface locked · {SurfaceDepthM:0.0}m arena · vs AI";
            Debug.Log($"[WRLDZ Surface] {Mode} locked depth={SurfaceDepthM:0.00}m width={SurfaceWidthM:0.00}m");
            Raise();
            return true;
        }

        /// <summary>Final config for <see cref="AppSession.StartArDuel"/>.</summary>
        public ArDuelMatchConfig BuildMatchConfig()
        {
            if (Draft == null) Begin(Mode);
            if (State != Phase.Ready)
            {
                // Fallback: use live span if any, else default stand
                var m = LiveMeters >= MinScanSpanM ? LiveMeters : ArDuelMatchConfig.DefaultStandM;
                LockAt(m);
            }

            Draft.Opponent = Mode == ScanMode.Pvp
                ? ArDuelOpponentKind.NearbyPeer
                : ArDuelOpponentKind.AiLocal;
            Draft.ClampSeparation();
            return Draft;
        }

        void Raise() => OnChanged?.Invoke();
    }
}
