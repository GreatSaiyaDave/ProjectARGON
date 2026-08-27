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
        Vector3 _baseLocalPos;
        bool _baseCaptured;
        /// <summary>True while a duel is live — idle keeps blade open; false keeps retracted.</summary>
        bool _duelActive;

        // Deployed pose targets (Battle City arm disk)
        public static readonly Quaternion BladeDeployedRot = Quaternion.identity;
        public static readonly Quaternion BladeRetractedRot = Quaternion.Euler(78f, 0f, -8f);
        public static readonly Vector3 BladeDeployedPos = new(0f, 0.02f, 0.04f);
        public static readonly Vector3 BladeRetractedPos = new(0.02f, 0.01f, -0.02f);

        public float EmissionBoost => _emissionBoost;
        public float Charge01 => _charge01;
        public float ScalePunch => _scalePunch;
        public float BladeOpen => _bladeOpen;
        public bool DuelActive => _duelActive;
        public DiskFxEvent Current => _evt;
        /// <summary>0–1 ring burst for deploy/summon energy sweep.</summary>
        public float EnergyPulse { get; private set; }

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
                // Animate from current open amount (usually 0) → 1
            }

            if (evt == DiskFxEvent.BladeRetract)
                _duelActive = false;
        }

        /// <summary>Mark duel live without a full deploy anim (already open).</summary>
        public void SetDuelActive(bool active)
        {
            _duelActive = active;
            if (!active && _evt == DiskFxEvent.Idle)
                _bladeOpen = Mathf.Min(_bladeOpen, 0.05f);
        }

        /// <summary>Snap instantly to retracted (pre-duel / overworld arm).</summary>
        public void SnapRetracted()
        {
            _duelActive = false;
            _bladeOpen = 0f;
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
            _bladeOpen = 1f;
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

            var spin = SpinSpeed * (_duelActive ? 1f : 0.12f);
            _emissionBoost = _duelActive
                ? 0.45f + 0.1f * Mathf.Sin(Time.time * 3f)
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
                    // Snap open like a Battle City disk arm (Yugi's disk deploy)
                    spin = SpinSpeed * (1.5f + 10f * (1f - u));
                    _bladeOpen = Mathf.SmoothStep(0f, 1f, u);
                    _emissionBoost = 0.5f + 3.2f * Mathf.Sin(u * Mathf.PI);
                    _scalePunch = 1f + 0.22f * Mathf.Sin(u * Mathf.PI);
                    EnergyPulse = Mathf.Sin(u * Mathf.PI);
                    _shake = (1f - u) * 0.025f * (u > 0.2f ? 1f : 0f);
                    if (u >= 1f)
                    {
                        _bladeOpen = 1f;
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
                    _emissionBoost = 0.55f + 0.9f * (0.5f + 0.5f * Mathf.Sin(Time.time * 9f));
                    _bladeOpen = Mathf.Max(_bladeOpen, 0.7f + 0.15f * Mathf.Sin(Time.time * 6f));
                    EnergyPulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 8f);
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

            if (bladePivot != null)
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
                if (_duelActive && t > 0.35f)
                {
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
            DiskFxEvent.BladeDeploy => 0.85f,
            DiskFxEvent.BladeRetract => 0.7f,
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
