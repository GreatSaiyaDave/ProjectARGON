using System.Collections;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Live anime simulator: summon pillars, attack beams, impacts, defend shields.
    /// Driven by the rules engine only — never invents resolutions.
    /// </summary>
    public class ArAnimeSequenceDirector : MonoBehaviour
    {
        public Camera StageCamera;
        public ArArenaHologramManager Arena;
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;
        public Transform ReferobotAnchor;

        DuelEngine _engine;
        ArFreeLookCamera _freeLook;
        Vector3 _camBasePos;
        Quaternion _camBaseRot;
        bool _camCaptured;
        Coroutine _seq;
        float _lastImpactT;
        int _lastAttackerId;
        bool _impactPlayedThisAttack;

        public void Bind(DuelEngine engine, Camera cam)
        {
            Unbind();
            _engine = engine;
            StageCamera = cam;
            _freeLook = cam != null ? cam.GetComponent<ArFreeLookCamera>() : null;
            if (_engine != null)
            {
                _engine.OnStateChanged += OnState;
                _engine.OnLog += OnLog;
            }

            ArArenaCombatFx.Ensure(transform, 28);
        }

        public void Unbind()
        {
            if (_engine != null)
            {
                _engine.OnStateChanged -= OnState;
                _engine.OnLog -= OnLog;
            }

            _engine = null;
            _camCaptured = false;
            HoldCinematicCamera(false);
            if (_seq != null)
            {
                StopCoroutine(_seq);
                _seq = null;
            }
        }

        void OnDestroy() => Unbind();

        void OnState()
        {
            if (_engine == null) return;

            if (_engine.HasDeclaredAttack && _engine.ActivePresentation != null)
            {
                var ap = _engine.ActivePresentation;
                if (ap.Kind == CombatActionKind.Attack)
                    PulseAttackCharge(ap);
            }
            else if (_lastAttackerId != 0 || _impactPlayedThisAttack)
            {
                Arena?.ClearAllAttackCharges();
                _impactPlayedThisAttack = false;
                _lastAttackerId = 0;
            }
        }

        void OnLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var lower = line.ToLowerInvariant();
            if (lower.Contains("normal summon") || lower.Contains("tribute summon") ||
                lower.Contains("special summon") || lower.Contains("flip summon") ||
                lower.Contains("summons "))
            {
                PlaySummonSequence();
            }
            else if (lower.Contains("activates") || lower.Contains("activate "))
            {
                PlayActivateSequence();
            }
            else if (lower.Contains("destroyed by battle") || lower.Contains("destroy"))
            {
                PlayImpact(heavy: true);
            }
            else if (lower.Contains("damage") && lower.Contains("→"))
            {
                PlayImpact(heavy: false);
            }
            else if (lower.Contains("defense") && lower.Contains("position"))
            {
                // Position change to DEF — shield on defender if we can find them
                PulseDefendFromLog();
            }
        }

        void PulseAttackCharge(ActiveCombatPresentation ap)
        {
            if (ap?.SourceCard == null) return;
            CaptureCam();

            var attackerIsPlayer = ap.Actor != null && ap.Actor.IsPlayer;
            var atkId = ap.SourceCard.InstanceId;
            if (atkId != _lastAttackerId)
            {
                _lastAttackerId = atkId;
                _impactPlayedThisAttack = false;
                Arena?.ClearAllAttackCharges();
            }

            var t = ap.ImpactFraction;
            Vector3 from = default, to = default;
            ArArenaCardVisual atkVis = null;
            ArArenaCardVisual defVis = null;

            if (Arena != null)
            {
                Arena.TryGetMonsterVisual(ap.SourceCard, attackerIsPlayer, out atkVis);
                if (ap.TargetCard != null)
                    Arena.TryGetMonsterVisual(ap.TargetCard, !attackerIsPlayer, out defVis);
            }

            if (atkVis != null)
            {
                from = atkVis.transform.position + Vector3.up * 0.3f;
                atkVis.SetAttackCharge(t);
            }
            else if ((attackerIsPlayer ? PlayerDisk : OppDisk) != null)
            {
                from = (attackerIsPlayer ? PlayerDisk : OppDisk).transform.position + Vector3.up * 0.25f;
            }

            if (defVis != null)
            {
                to = defVis.transform.position + Vector3.up * 0.3f;
                // Defender shield if face-up DEF or always a light guard during charge
                if (ap.TargetCard != null &&
                    (ap.TargetCard.Position == BattlePosition.Defense || !ap.TargetCard.FaceUp))
                {
                    // One-shot shield when charge starts ramping
                    if (t > 0.15f && t < 0.25f)
                        ArArenaCombatFx.PlayDefendShield(defVis.transform.position, defVis.PlayerSide);
                }
            }
            else if (ap.TargetCard == null && Arena?.ArenaRoot != null)
            {
                // Direct attack — aim at opp disk / far side of board
                to = attackerIsPlayer
                    ? (OppDisk != null
                        ? OppDisk.transform.position + Vector3.up * 0.2f
                        : Arena.ArenaRoot.position + Arena.ArenaRoot.forward * 0.6f + Vector3.up * 0.3f)
                    : (PlayerDisk != null
                        ? PlayerDisk.transform.position + Vector3.up * 0.2f
                        : Arena.ArenaRoot.position - Arena.ArenaRoot.forward * 0.6f + Vector3.up * 0.3f);
            }
            else if (Arena?.ArenaRoot != null)
            {
                to = Arena.ArenaRoot.position + Vector3.up * 0.35f;
            }

            // Sparse beams (0.28s) — charge aura on the card is continuous & cheap
            if (from != default && to != default && t > 0.25f &&
                Time.unscaledTime - _lastImpactT > 0.28f)
            {
                ArArenaCombatFx.PlayAttackBeam(from, to, attackerIsPlayer, t);
                _lastImpactT = Time.unscaledTime;
            }

            // Camera push toward impact point — never steal a user look/walk
            if (StageCamera != null && to != default && !UserOwnsCamera())
            {
                HoldCinematicCamera(true);
                var look = Vector3.Lerp(from != default ? from : to, to, 0.55f);
                StageCamera.transform.position = Vector3.Lerp(
                    _camBasePos,
                    Vector3.Lerp(_camBasePos, look + Vector3.up * 0.4f - StageCamera.transform.forward * 0.3f, 0.35f),
                    t * 0.4f);
                StageCamera.transform.rotation = Quaternion.Slerp(
                    _camBaseRot,
                    Quaternion.LookRotation(look - StageCamera.transform.position, Vector3.up),
                    t * 0.45f);
            }

            (attackerIsPlayer ? PlayerDisk : OppDisk)?.PlayFx(DiskFxEvent.AttackCharge);

            if (t > 0.88f && !_impactPlayedThisAttack)
            {
                _impactPlayedThisAttack = true;
                if (defVis != null)
                    defVis.PlayHitFlash();
                else
                    PlayImpact(heavy: ap.TargetCard != null);
                // Impact FX remains; full shatter plays when engine removes the card from field
                (attackerIsPlayer ? OppDisk : PlayerDisk)?.PlayFx(DiskFxEvent.ImpactHit);
                (attackerIsPlayer ? PlayerDisk : OppDisk)?.PlayFx(DiskFxEvent.DamageShake);
            }
        }

        void PulseDefendFromLog()
        {
            if (_engine == null || Arena == null) return;
            // Best-effort: pulse shield on any face-up DEF monster that just changed
            foreach (var who in new[] { _engine.Player, _engine.Opponent })
            {
                if (who == null) continue;
                for (var i = 0; i < who.MonsterZones.Length; i++)
                {
                    var m = who.MonsterZones[i].Occupant;
                    if (m == null || !m.FaceUp || m.Position != BattlePosition.Defense) continue;
                    if (Arena.TryGetMonsterVisual(m, who.IsPlayer, out var vis) && vis != null)
                        ArArenaCombatFx.PlayDefendShield(vis.transform.position, vis.PlayerSide);
                }
            }
        }

        public void PlaySummonSequence()
        {
            if (_seq != null) StopCoroutine(_seq);
            _seq = StartCoroutine(SummonCo());
        }

        IEnumerator SummonCo()
        {
            CaptureCam();
            if (StageCamera != null && Arena?.ArenaRoot != null && !UserOwnsCamera())
            {
                HoldCinematicCamera(true);
                var look = Arena.ArenaRoot.TransformPoint(
                    ArPlaymatLayout.MonsterArenaLocal(2, true)) + Vector3.up * 0.95f;
                var t = 0f;
                while (t < 1f && !UserOwnsCamera())
                {
                    t += Time.deltaTime / 0.5f;
                    var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                    StageCamera.transform.position = Vector3.Lerp(_camBasePos,
                        _camBasePos + StageCamera.transform.right * 0.06f + Vector3.up * 0.08f, u * 0.55f);
                    StageCamera.transform.rotation = Quaternion.Slerp(_camBaseRot,
                        Quaternion.LookRotation(look - StageCamera.transform.position), u * 0.65f);
                    yield return null;
                }

                if (!UserOwnsCamera())
                {
                    yield return new WaitForSeconds(0.2f);
                    t = 0f;
                    var p0 = StageCamera.transform.position;
                    var r0 = StageCamera.transform.rotation;
                    while (t < 1f && !UserOwnsCamera())
                    {
                        t += Time.deltaTime / 0.35f;
                        var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                        StageCamera.transform.position = Vector3.Lerp(p0, _camBasePos, u);
                        StageCamera.transform.rotation = Quaternion.Slerp(r0, _camBaseRot, u);
                        yield return null;
                    }
                }
            }

            HoldCinematicCamera(false);
            _seq = null;
        }

        public void PlayActivateSequence()
        {
            if (Arena?.ArenaRoot != null)
                ArArenaCombatFx.PlaySpellCast(Arena.ArenaRoot.position + Vector3.up * 0.35f, true);
        }

        public void PlayImpact(bool heavy = true)
        {
            OppDisk?.PlayFx(DiskFxEvent.ImpactHit);
            PlayerDisk?.PlayFx(DiskFxEvent.DamageShake);
            if (Arena?.ArenaRoot != null)
                ArArenaCombatFx.PlayImpact(Arena.ArenaRoot.position + Vector3.up * 0.35f, heavy);
        }

        void CaptureCam()
        {
            if (StageCamera == null) return;
            if (UserOwnsCamera()) return;
            if (!_camCaptured || !_engine.HasDeclaredAttack)
            {
                _camBasePos = StageCamera.transform.position;
                _camBaseRot = StageCamera.transform.rotation;
                _camCaptured = true;
            }
        }

        bool UserOwnsCamera()
        {
            if (_freeLook == null && StageCamera != null)
                _freeLook = StageCamera.GetComponent<ArFreeLookCamera>();
            return _freeLook != null && _freeLook.UserMoved;
        }

        void HoldCinematicCamera(bool on)
        {
            if (_freeLook == null) return;
            _freeLook.SuppressApply = on && !_freeLook.UserMoved;
        }

        void LateUpdate()
        {
            if (_engine == null || StageCamera == null) return;
            if (UserOwnsCamera())
            {
                _camCaptured = false;
                HoldCinematicCamera(false);
                return;
            }

            if (_engine.HasDeclaredAttack) return;
            if (_seq != null) return;
            if (!_camCaptured)
            {
                HoldCinematicCamera(false);
                return;
            }

            HoldCinematicCamera(true);
            StageCamera.transform.position = Vector3.Lerp(
                StageCamera.transform.position, _camBasePos, Time.deltaTime * 2.5f);
            StageCamera.transform.rotation = Quaternion.Slerp(
                StageCamera.transform.rotation, _camBaseRot, Time.deltaTime * 2.5f);
            if (Vector3.Distance(StageCamera.transform.position, _camBasePos) < 0.004f &&
                Quaternion.Angle(StageCamera.transform.rotation, _camBaseRot) < 0.4f)
            {
                StageCamera.transform.position = _camBasePos;
                StageCamera.transform.rotation = _camBaseRot;
                _camCaptured = false;
                HoldCinematicCamera(false);
            }
        }
    }
}
