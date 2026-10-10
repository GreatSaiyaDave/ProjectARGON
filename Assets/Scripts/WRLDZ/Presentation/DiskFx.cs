using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Anime disk animations / VFX state for a Spirit Dueler Battle City disk.
    /// Driven by duel events (summon, set, activate, attack charge, idle).
    /// </summary>
    public enum DiskFxEvent
    {
        Idle = 0,
        /// <summary>Blade deploys / opens — anime disk arm snap at duel start.</summary>
        BladeDeploy,
        /// <summary>Blade folds to compact wrist form — duel inactive / end.</summary>
        BladeRetract,
        /// <summary>Card materializes onto a zone (summon face-up).</summary>
        SummonFlash,
        /// <summary>Face-down set slides into the disk.</summary>
        SetSlide,
        /// <summary>Trap/spell activate — disk lights burst.</summary>
        ActivateBurst,
        /// <summary>Hologram projects above the field.</summary>
        HoloProject,
        AttackCharge,
        ImpactHit,
        DamageShake,
        LegalZonePulse
    }

    /// <summary>
    /// Anime Battle City disk motion — retract/deploy blade, idle energy,
    /// event flashes (manga/anime duel disk).
    /// When duel is inactive, blade stays fully retracted (folded on the arm).
    /// </summary>
    public class DiskFxDriver
    {
        public float SpinSpeed = 8f;
        public float HoverAmp = 0.028f;
        /// <summary>The Battle City activation (SpiritDuelerMechanics): extend · sweep and meet · slide to the lock, with
        /// the reference's pauses. PreDuelCinematic and the rig's deploy routine wait on this.</summary>
        public const float DeploySeconds = 2.4f;
        /// <summary>
        /// The kit parts carry the deploy / retract (<see cref="ArInteraction.SpiritDuelerMechanics"/>): BladePivot
        /// stays in the play pose and only jolts on impacts, instead of the whole disk tilting and shrinking.
        /// </summary>
        public bool Mechanical;
        public Color Accent = new(0.25f, 0.9f, 1f, 1f);

        float _eventT;
        DiskFxEvent _evt = DiskFxEvent.Idle;
        float _eventDuration = 0.5f;
        float _shake;
        float _emissionBoost;
        float _charge01;
        float _scalePunch = 1f;
        /// <summary>0 = fully retracted (compact wrist) · 1 = fully deployed field.</summary>
        float _bladeOpen;
        /// <summary>Blade open amount when the current BladeDeploy started (deploy resumes from here).</summary>
        float _bladeFrom;
        /// <summary>Tray position when the current BladeDeploy started (an already-open tray never re-locks).</summary>
        float _trayFrom;
        Vector3 _baseLocalPos;
        bool _baseCaptured;
        /// <summary>True while a duel is live — idle keeps blade open; false keeps retracted.</summary>
        bool _duelActive;
        float _mech;
        /// <summary>Where the mechanism is heading (0 standby, 1 locked) and how long a full travel takes. The
        /// mechanism advances toward its target every tick whatever event is playing, so a pulse fired while a
        /// deploy is running (the cinematic's LP power-on HoloProject landed at 95 % of the travel, after the rig's
        /// 0.28 s skin fade had pushed the start back) can no longer freeze the plate short of the lock.</summary>
        float _mechTarget;
        float _mechSeconds = DeploySeconds;
        /// <summary>While true, the standby hub/plate emission drops to near-zero so the
        /// coin/die hologram reads clean (PreDuelCinematic.DecideFirstTurn). Cleared by
        /// BladeDeploy, SnapDeployed and SnapRetracted.</summary>
        bool _standbyGlowSuppressed;

        // Deployed pose targets (Battle City arm disk)
        public static readonly Quaternion BladeDeployedRot = Quaternion.identity;
        public static readonly Quaternion BladeRetractedRot = Quaternion.Euler(78f, 0f, -8f);
        public static readonly Vector3 BladeDeployedPos = new(0f, 0.02f, 0.04f);
        public static readonly Vector3 BladeRetractedPos = new(0.02f, 0.01f, -0.02f);

        public float EmissionBoost => _emissionBoost;
        public float Charge01 => _charge01;
        public float ScalePunch => _scalePunch;
        public float BladeOpen => _bladeOpen;
        /// <summary>
        /// Deploy progress of the mechanism, 0 standby .. 1 play (linear in time; the mechanics shape it). Only
        /// BladeDeploy, BladeRetract and the snaps move it: the event pulses that dip <see cref="BladeOpen"/>
        /// never fold the blades.
        /// </summary>
        public float MechOpen => _mech;
        public bool DuelActive => _duelActive;
        public DiskFxEvent Current => _evt;
        /// <summary>0–1 ring burst for deploy/summon energy sweep.</summary>
        public float EnergyPulse { get; private set; }
        /// <summary>
        /// 0 = graveyard locked over the deck (standby). 1 = tray slid out.
        /// The anime unlocks the tray after the plate has started to unfold.
        /// </summary>
        public float GraveyardTray { get; private set; }

        public void Play(DiskFxEvent evt, float duration = -1f)
        {
            _evt = evt;
            _eventT = 0f;
            _eventDuration = duration > 0f ? duration : DefaultDuration(evt);
            if (evt == DiskFxEvent.AttackCharge)
                _charge01 = 0f;
            if (evt == DiskFxEvent.BladeDeploy)
            {
                _duelActive = true;
                _standbyGlowSuppressed = false;
                // Animate from the current open amount (usually 0) → 1. Re-triggering on an open plate is an
                // energy pulse only: the plate must not snap shut and re-unfold (PreDuel shuffle beat, QC M1).
                _bladeFrom = Mathf.Clamp01(_bladeOpen);
                _trayFrom = GraveyardTray;
                _mechTarget = 1f;
                _mechSeconds = _eventDuration;
            }

            if (evt == DiskFxEvent.BladeRetract)
            {
                _duelActive = false;
                _mechTarget = 0f;
                _mechSeconds = _eventDuration;
            }
        }

        /// <summary>Mark duel live without a full deploy anim (already open).</summary>
        public void SetDuelActive(bool active)
        {
            _duelActive = active;
            if (!active && _evt == DiskFxEvent.Idle)
                _bladeOpen = Mathf.Min(_bladeOpen, 0.05f);
        }

        /// <summary>Dim the standby hub/plate glow (coin/dice beat); false restores it.</summary>
        public void SuppressStandbyGlow(bool suppress) => _standbyGlowSuppressed = suppress;

        /// <summary>Snap instantly to retracted (pre-duel / overworld arm).</summary>
        public void SnapRetracted()
        {
            _duelActive = false;
            _standbyGlowSuppressed = false;
            _bladeOpen = 0f;
            _mech = 0f;
            _mechTarget = 0f;
            GraveyardTray = 0f;
            _evt = DiskFxEvent.Idle;
            _emissionBoost = 0.15f;
            EnergyPulse = 0f;
            _scalePunch = 1f;
        }

        /// <summary>Lock hover to the current DiskRoot pose (after wrist/deck calibrate).</summary>
        public void CaptureBase(Transform diskRoot)
        {
            if (diskRoot == null) return;
            _baseLocalPos = diskRoot.localPosition;
            _baseCaptured = true;
        }

        /// <summary>Snap instantly to full deploy (skip intro).</summary>
        public void SnapDeployed()
        {
            _duelActive = true;
            _standbyGlowSuppressed = false;
            _bladeOpen = 1f;
            _mech = 1f;
            _mechTarget = 1f;
            GraveyardTray = 1f;
            _evt = DiskFxEvent.Idle;
            _emissionBoost = 0.55f;
        }

        public void SetCharge01(float t) => _charge01 = Mathf.Clamp01(t);

        public void Tick(float dt, Transform diskRoot, Material diskMat)
        {
            Tick(dt, diskRoot, diskMat, null, null, null);
        }

        /// <summary>
        /// Full tick with optional blade hierarchy for true retract/deploy poses.
        /// </summary>
        public void Tick(float dt, Transform diskRoot, Material diskMat,
            Transform bladePivot, Transform zonesRoot, Transform energyRing)
        {
            if (diskRoot != null && !_baseCaptured)
            {
                _baseLocalPos = diskRoot.localPosition;
                _baseCaptured = true;
            }

            _eventT += dt;
            var u = _eventDuration <= 0.001f ? 1f : Mathf.Clamp01(_eventT / _eventDuration);
            // the mechanism runs at its own constant speed toward its target, independent of the event pulses
            _mech = Mathf.MoveTowards(_mech, _mechTarget, dt / Mathf.Max(0.05f, _mechSeconds));

            var spin = SpinSpeed * (_duelActive ? 1f : 0.12f);
            _emissionBoost = _duelActive
                ? 0.45f + 0.1f * Mathf.Sin(Time.time * 3f)
                : _standbyGlowSuppressed
                    ? 0.02f
                    : 0.12f + 0.04f * Mathf.Sin(Time.time * 1.5f);
            _shake = 0f;
            _scalePunch = Mathf.Lerp(_scalePunch, 1f, dt * 6f);
            EnergyPulse = Mathf.Lerp(EnergyPulse, 0f, dt * 2.5f);

            switch (_evt)
            {
                case DiskFxEvent.Idle:
                    // Active duel: blade stays open with subtle energy breathe
                    // Inactive: stay retracted on the arm
                    var idleTarget = _duelActive ? 0.95f : 0f;
                    _bladeOpen = Mathf.Lerp(_bladeOpen, idleTarget, dt * (_duelActive ? 2.5f : 4f));
                    if (_duelActive)
                        EnergyPulse = 0.15f + 0.08f * Mathf.Sin(Time.time * 2.2f);
                    break;

                case DiskFxEvent.BladeDeploy:
                    // Version 2 standby → duel: plate unfolds, then the graveyard
                    // tray unlocks. Lights sweep the hub for the whole travel.
                    spin = SpinSpeed * (1.5f + 10f * (1f - u));
                    _bladeOpen = Mathf.Lerp(_bladeFrom, 1f, Mathf.SmoothStep(0f, 1f, u));
                    _emissionBoost = 0.5f + 3.2f * Mathf.Sin(u * Mathf.PI);
                    _scalePunch = 1f + 0.22f * Mathf.Sin(u * Mathf.PI);
                    EnergyPulse = Mathf.Sin(u * Mathf.PI);
                    _shake = (1f - u) * 0.025f * (u > 0.2f ? 1f : 0f);
                    if (u >= 1f)
                    {
                        _bladeOpen = 1f;
                        _mech = 1f;
                        _duelActive = true;
                        _evt = DiskFxEvent.Idle;
                    }

                    break;

                case DiskFxEvent.BladeRetract:
                    // Fold shut — anime end-of-duel / leave AR
                    spin = SpinSpeed * 0.4f * (1f - u);
                    _bladeOpen = Mathf.SmoothStep(1f, 0f, u);
                    _emissionBoost = 0.55f * (1f - u) + 0.1f;
                    EnergyPulse = (1f - u) * 0.6f;
                    _scalePunch = 1f - 0.08f * u;
                    if (u >= 1f)
                    {
                        _bladeOpen = 0f;
                        _mech = 0f;
                        _duelActive = false;
                        _evt = DiskFxEvent.Idle;
                    }

                    break;

                case DiskFxEvent.SummonFlash:
                    spin = SpinSpeed * (1f + 6f * (1f - u));
                    _emissionBoost = 0.5f + 3.2f * Mathf.Sin(u * Mathf.PI);
                    _scalePunch = 1f + 0.25f * (1f - u) * Mathf.Sin(u * Mathf.PI * 2f);
                    _bladeOpen = Mathf.Max(_bladeOpen, 0.85f + 0.15f * Mathf.Sin(u * Mathf.PI));
                    EnergyPulse = Mathf.Max(EnergyPulse, Mathf.Sin(u * Mathf.PI));
                    _shake = (1f - u) * 0.03f;
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.SetSlide:
                    spin = SpinSpeed * 0.35f;
                    _emissionBoost = 0.45f + 0.9f * (1f - u);
                    _bladeOpen = Mathf.Max(_bladeOpen, 0.75f);
                    _scalePunch = 1f + 0.08f * (1f - u);
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.ActivateBurst:
                    spin = SpinSpeed * (3f + 10f * (1f - u));
                    _emissionBoost = 0.8f + 4f * (1f - u);
                    _scalePunch = 1f + 0.3f * (1f - u);
                    _bladeOpen = 1f;
                    EnergyPulse = 1f - u;
                    _shake = (1f - u) * 0.05f;
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.HoloProject:
                    spin = SpinSpeed * 1.5f;
                    _emissionBoost = 0.7f + 2f * Mathf.Sin(u * Mathf.PI);
                    _bladeOpen = Mathf.Max(_bladeOpen, 0.9f);
                    EnergyPulse = Mathf.Sin(u * Mathf.PI) * 0.85f;
                    _scalePunch = 1f + 0.12f * Mathf.Sin(u * Mathf.PI);
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.AttackCharge:
                    spin = SpinSpeed * (1f + 4f * _charge01);
                    _emissionBoost = 0.5f + 3f * _charge01;
                    _bladeOpen = 0.85f + 0.15f * _charge01;
                    EnergyPulse = 0.3f + 0.7f * _charge01;
                    _scalePunch = 1f + 0.1f * _charge01;
                    break;

                case DiskFxEvent.ImpactHit:
                    _shake = (1f - u) * 0.09f;
                    _emissionBoost = 1.2f + 3f * (1f - u);
                    _scalePunch = 1f + 0.2f * (1f - u);
                    EnergyPulse = 1f - u;
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.DamageShake:
                    _shake = (1f - u) * 0.06f;
                    if (u >= 1f) _evt = DiskFxEvent.Idle;
                    break;

                case DiskFxEvent.LegalZonePulse:
                    // One soft breath (drag start / hand shuffle), not a strobe, and the plate holds still so the
                    // lit zones are easy to hit. Mid-duel it hands back to Idle; the pre-duel shuffle keeps the
                    // blade held open until BladeDeploy takes over, as before.
                    var breathe = Mathf.Sin(u * Mathf.PI);
                    _emissionBoost = 0.45f + 0.35f * breathe;
                    _bladeOpen = Mathf.Max(_bladeOpen, 0.75f);
                    EnergyPulse = Mathf.Max(EnergyPulse, 0.3f * breathe);
                    if (u >= 1f && _duelActive) _evt = DiskFxEvent.Idle;
                    break;
            }

            // Root hover / shake (wrist base stays stable; small motion when open)
            if (diskRoot != null)
            {
                var hover = Mathf.Sin(Time.time * 1.6f) * HoverAmp * _bladeOpen;
                var sx = (Mathf.PerlinNoise(Time.time * 22f, 0.1f) - 0.5f) * 2f * _shake;
                var sz = (Mathf.PerlinNoise(0.3f, Time.time * 22f) - 0.5f) * 2f * _shake;
                var basePos = _baseCaptured ? _baseLocalPos : Vector3.zero;
                // Arm disks stay locked — no continuous yaw spin (scrambles zone layout in AR).
                // Only tiny hover / hit-shake.
                diskRoot.localPosition = basePos + new Vector3(sx * 0.35f, hover * 0.5f, sz * 0.35f);
            }

            // Graveyard stays locked over the deck until the plate is well into the unfold.
            var trayTarget = 0f;
            if (_evt == DiskFxEvent.BladeDeploy)
            {
                var trayU = Mathf.InverseLerp(0.38f, 1f, u);
                trayTarget = trayU <= 0f ? 0f : trayU * trayU * (3f - 2f * trayU);
                // A re-deploy on an already-unlocked tray keeps it out (never retract toward 0 mid-duel).
                trayTarget = Mathf.Max(trayTarget, _trayFrom);
            }
            else if (_duelActive && _bladeOpen > 0.72f)
                trayTarget = 1f;
            GraveyardTray = Mathf.MoveTowards(GraveyardTray, trayTarget, dt * 2.4f);

            // Blade pivot + zones — real retract/deploy
            ApplyBladeHierarchy(bladePivot, zonesRoot, energyRing, dt);

            if (diskMat != null && diskMat.HasProperty("_EmissionColor"))
            {
                diskMat.EnableKeyword("_EMISSION");
                diskMat.SetColor("_EmissionColor", Accent * _emissionBoost);
            }
        }

        void ApplyBladeHierarchy(Transform bladePivot, Transform zonesRoot, Transform energyRing, float dt)
        {
            var t = Mathf.Clamp01(_bladeOpen);
            var smooth = t * t * (3f - 2f * t); // smoothstep

            if (bladePivot != null && Mechanical)
            {
                bladePivot.localRotation = BladeDeployedRot;
                bladePivot.localPosition = BladeDeployedPos;
                // an impact reads as a jolt through the disk, not the disk ballooning: a third of the old punch
                bladePivot.localScale = Vector3.one * (1f + (_scalePunch - 1f) * 0.3f);
            }
            else if (bladePivot != null)
            {
                bladePivot.localRotation = Quaternion.Slerp(BladeRetractedRot, BladeDeployedRot, smooth);
                bladePivot.localPosition = Vector3.Lerp(BladeRetractedPos, BladeDeployedPos, smooth);
                // Compact scale when folded (thinner silhouette on arm)
                var sc = Mathf.Lerp(0.42f, 1f, smooth) * _scalePunch;
                var thin = Mathf.Lerp(0.55f, 1f, smooth);
                bladePivot.localScale = new Vector3(sc, thin, sc);
            }

            if (zonesRoot != null)
            {
                // Must match the disk mesh scale or pad cards sink into the hub.
                var meshS = ArInteraction.ArPlaymatLayout.DiskMeshVisualScale;
                if (Mechanical || (_duelActive && t > 0.35f))
                {
                    // The kit's hub never shrinks: the zone root carries the deck holder, the graveyard tray, the LP
                    // counter and the zone anchors in mesh-local metres, so it stays at mesh scale through the whole
                    // activation. The ramp below belongs to the old folding blade (it drew the deck 72 % of the way
                    // in toward the hub centre — 35 % on a phone — until the plate was most of the way open).
                    zonesRoot.localScale = Vector3.one * meshS;
                }
                else
                {
                    var zVis = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, t));
                    var minZ = Application.isEditor ? 0.72f : 0.35f;
                    zonesRoot.localScale = Vector3.one * (meshS * (minZ + (1f - minZ) * zVis));
                }
            }

            if (energyRing != null)
            {
                // Keep the empty FX hook off-camera — no leftover ring mesh on the plate.
                energyRing.gameObject.SetActive(false);
            }
        }

        static float DefaultDuration(DiskFxEvent e) => e switch
        {
            DiskFxEvent.BladeDeploy => DeploySeconds,   // the sections extend, sweep round and meet, the plate slides out, lock (art bar: readable)
            DiskFxEvent.BladeRetract => 1.1f,
            DiskFxEvent.SummonFlash => 0.95f,
            DiskFxEvent.SetSlide => 0.6f,
            DiskFxEvent.ActivateBurst => 0.85f,
            DiskFxEvent.HoloProject => 0.9f,
            DiskFxEvent.ImpactHit => 0.5f,
            DiskFxEvent.DamageShake => 0.4f,
            _ => 0.5f
        };
    }
}
